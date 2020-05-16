using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.XPath;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;

namespace MapFixer
{
    class Program
    {
        static void Main(string[] args)
        {
            ParseSettings(args);

            _gameDir = Path.GetDirectoryName(Path.GetDirectoryName(Directory.GetCurrentDirectory()));

            WorkingThread();
        }

        static string _gameDir;

        static bool _verbose; // -verbose

        static string _FSGameFolderNameLog; // -FSGameFolderNameLog=""
        static List<string> _FSGameFolderNameOutputs = new List<string>(); // -FSGameFolderNameOutputs=""

        static void ParseSettings(string[] args)
        {
            foreach (string arg in args)
            {
                string[] argOptions = arg.Split('=');
                string argName = argOptions[0];

                switch (argName)
                {
                    case "-verbose":
                        _verbose = true;
                        break;
                    case "-FSGameFolderNameLog":
                        _FSGameFolderNameLog = argOptions[1];
                        break;
                    case "-FSGameFolderNameOutputs":
                        _FSGameFolderNameOutputs.Add(argOptions[1]);
                        break;
                    default:
                        throw new ApplicationException("Unknown input arg '" + arg + "'");
                }
            }

            if (_verbose)
            {
                Console.WriteLine("-verbose " + _verbose.ToString());
                Console.WriteLine("-FSGameFolderNameLog " + _FSGameFolderNameLog);
                foreach (string output in _FSGameFolderNameOutputs)
                    Console.WriteLine("-FSGameFolderNameOutputs " + output);
            }
        }

        static void WorkingThread()
        {
            string filePath = Path.Combine(Path.Combine(_gameDir, _FSGameFolderNameLog), "games_mp.log");

            DateTime lastUpdate = File.GetLastWriteTime(filePath);

            FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
            StreamReader sr = new StreamReader(fs);
            string[] lines = sr.ReadToEnd().Split('\n');
            sr.Close();
            fs.Close();

            int lastLine = lines.Length - 1;

            bool isInGameLast = true;
            bool isInGame = false;

            while (true)
            {
                Thread.Sleep(50);

                if (isInGameLast != isInGame)
                {
                    isInGameLast = isInGame;

                    ClearCurrentConsoleLine();
                    if (isInGame)
                        Console.Write("Quit the game...");
                    else
                        Console.Write("Start the game...");
                }

                if (File.GetLastWriteTime(filePath) <= lastUpdate)
                    continue;

                try
                {
                    fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    sr = new StreamReader(fs);
                    lines = sr.ReadToEnd().Split('\n');
                    sr.Close();
                    fs.Close();

                    ReadLast(lines, lastLine);

                    lastLine = lines.Length - 1;
                    lastUpdate = File.GetLastWriteTime(filePath);

                    isInGame = false;
                }
                catch (IOException)
                {
                    isInGame = true;
                }
            }
        }

        public static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            for (int i = 0; i < Console.WindowWidth; i++)
                Console.Write(" ");
            Console.SetCursorPosition(0, currentLineCursor);
        }

        static void ReadLast(string[] lines, int startLine)
        {
            bool isStarted = false;
            List<string> filePathes = new List<string>();
            List<StringBuilder> fileContents = new List<StringBuilder>();

            string mainPath = null;
            string curPath = null;
            StringBuilder curSb = new StringBuilder();

            for (int i = lines.Length - 1; i >= startLine; i--)
            {
                string line = lines[i].TrimEnd('\r');

                if (line.EndsWith("== MAPFIXER END ============================================================="))
                    isStarted = true;
                else if (isStarted && line.EndsWith("== MAPFIXER START ============================================================="))
                {
                    isStarted = false;

                    if (!filePathes.Contains(curPath))
                    {
                        filePathes.Add(curPath);
                        fileContents.Add(curSb);
                    }

                    curPath = null;
                    curSb = new StringBuilder();
                }
                else if (isStarted)
                {
                    line = RemoveTime(line);

                    if (line.StartsWith("MFilePath: "))
                        mainPath = line.Substring("MFilePath: ".Length);
                    else if (line.StartsWith("FilePath: "))
                        curPath = line.Substring("FilePath: ".Length);
                    else
                        curSb.Insert(0, line + Environment.NewLine);
                }
            }

            for (int i = 0; i < filePathes.Count; i++)
            {
                curPath = filePathes[i];
                curSb = fileContents[i];

                Console.WriteLine(Environment.NewLine + "=======================================" + Environment.NewLine);
                Console.WriteLine(DateTime.Now + " ; " + curPath);
                if (_verbose)
                    Console.Write(curSb.ToString());
                Console.WriteLine(Environment.NewLine + "=======================================" + Environment.NewLine);

                foreach (string outputFSGame in _FSGameFolderNameOutputs)
                {
                    string fullPath = Path.Combine(Path.Combine(_gameDir, outputFSGame), curPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllText(fullPath, curSb.ToString());
                }
            }

            if (filePathes.Count > 0)
            {
                foreach (string outputFSGame in _FSGameFolderNameOutputs)
                {
                    string fullMainPath = Path.Combine(Path.Combine(_gameDir, outputFSGame), mainPath);
                    string fullParentDir = Path.Combine(Path.Combine(_gameDir, outputFSGame), Path.GetDirectoryName(filePathes[0]));

                    ModifyMainFile(fullMainPath, fullParentDir);
                }
            }
        }

        static string RemoveTime(string line)
        {
            line = line.TrimStart();
            int i = line.IndexOf(' ');
            return line.Substring(i + 1);
        }

        static void ModifyMainFile(string mainFilePath, string filesParentDir)
        {
            string mainContent = File.ReadAllText(mainFilePath);
            StringBuilder mainSb = new StringBuilder(mainContent);
            string mainSbWithoutComments = mainContent;

            // load settings from XML
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(GetXMLSettings(mainContent));

            string commentRegex = ((XmlElement)doc.SelectSingleNode("MapFixer/Comments")).GetAttribute("regex");
            string switchRegex = ((XmlElement)doc.SelectSingleNode("MapFixer/Switch")).GetAttribute("regex");
            int switchContentGroup = Int32.Parse(((XmlElement)doc.SelectSingleNode("MapFixer/Switch")).GetAttribute("contentGroup"));
            string caseFormat = ((XmlElement)doc.SelectSingleNode("MapFixer/Switch/Case")).GetAttribute("strFormat");
            string defaultFormat = ((XmlElement)doc.SelectSingleNode("MapFixer/Switch/Default")).GetAttribute("strFormat");

            // replace comments with spaces
            MatchCollection comments = Regex.Matches(mainContent, commentRegex, RegexOptions.CultureInvariant);
            if (comments.Count > 0)
            {
                StringBuilder sb = new StringBuilder(mainSbWithoutComments);
                foreach (Match m in comments)
                {
                    sb.Remove(m.Index, m.Length);
                    sb.Insert(m.Index, new String(' ', m.Length));
                }
                mainSbWithoutComments = sb.ToString();
            }

            // remove switch content
            Match switchM = Regex.Match(mainSbWithoutComments, switchRegex, RegexOptions.CultureInvariant);
            mainSb.Remove(switchM.Groups[switchContentGroup].Index, switchM.Groups[switchContentGroup].Length);
            int mainSbInsertI = switchM.Groups[switchContentGroup].Index;

            // insert new switch content
            string[] files = Directory.GetFiles(filesParentDir, "*.gsc", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                if (file == mainFilePath)
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(file);
                string insertCase = String.Format(caseFormat.Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\\", "\\"), fileName);
                mainSb.Insert(mainSbInsertI, insertCase);
                mainSbInsertI += insertCase.Length;
            }

            mainSb.Insert(mainSbInsertI, defaultFormat.Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\\", "\\"));

            File.WriteAllText(mainFilePath, mainSb.ToString(), ASCIIEncoding.GetEncoding(1250));
        }

        static string GetXMLSettings(string fileContent)
        {
            var linesQuerry = from line in fileContent.Split('\n')
                              select line.TrimEnd('\r');

            StringBuilder sb = new StringBuilder();
            foreach (string l in linesQuerry)
            {
                if (!l.StartsWith("///"))
                    break;

                sb.Append(l.Substring("///".Length) + "\r\n");
            }

            return sb.ToString();
        }
    }
}
