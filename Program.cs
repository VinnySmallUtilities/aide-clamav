using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace aide_clamav;
public class Program
{
    public enum Errors
    {
        success                 = 0,
        help                    = 1,

        confFileNotExists       = 101,
        reportFileNotExists     = 102,
        clamscanFileNotExists   = 103,

        confFileLengthIncorrect = 1011
    };

    public static readonly string Version = "2023.04.27";
    public static int Main(string[] args)
    {
        if (args.Length < 1 || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
        {
            PrintHelp();
            return (int) Errors.help;
        }

        if (args[0] == "?")
        {
            args[0] = "aide-clamav.conf";
        }

        var confFile = new FileInfo(args[0]);
        confFile.Refresh();
        if (!confFile.Exists)
        {
            Console.Error.WriteLine($"The configuration file not exists: \"{confFile.FullName}\"");
            return (int) Errors.confFileNotExists;
        }

        var confLines  = File.ReadAllLines(confFile.FullName);

        if (confFile.Length < 3)
        {
            Console.Error.WriteLine($"Configuration file incorrect: three lines required, but there are {confFile.Length} only");
            PrintHelp();
            return (int) Errors.confFileLengthIncorrect;
        }

        var reportFile = new FileInfo(confLines[0]);
        reportFile.Refresh();

        if (!reportFile.Exists)
        {
            Console.Error.WriteLine($"The AIDE report.log file not exists: \"{reportFile.FullName}\"");
            return (int) Errors.reportFileNotExists;
        }

        var clamscanFile = confLines[1];
        var clamscanArgs = confLines[2];

        if (confLines.Length > 3)
        {
            Int32.TryParse(confLines[3], out clamscanThreads);
            if (clamscanThreads < 1 || clamscanThreads > Environment.ProcessorCount)
            {
                Console.Error.WriteLine("Fourth line in configuration file: clamscanThreads is incorrect. Setted to 1");
                clamscanThreads = Environment.ProcessorCount;
            }
        }

        var reportLines = File.ReadAllLines(reportFile.FullName);

        var fs = new List<string>(reportLines.Length);
        // foreach (var line in reportLines)
        Parallel.ForEach
        (
            reportLines,
            delegate (string line, ParallelLoopState _state, long _)
            {
                if (!line.Contains(":"))
                    return;

                var sLine = line.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (sLine.Length < 2)
                    return;

                // Получаем имя файла для антивирусной проверки
                var fi = new FileInfo(sLine[1]);
                fi.Refresh();
                if (!fi.Exists)
                    return;

                // ExecClamScan(fi, clamscanFile, clamscanArgs);
                lock (fs)
                    fs.Add(fi.FullName);
            }
        );

        fs.Sort();

        string lastFileName = "";
        var count    = fs.Count / clamscanThreads;
        var curList  = new List<string>(count + 1);
        for (int i = 0; i < fs.Count; i++)
        {
            // Не повторяем файлы, если вдруг они повторно перечисленны в списке
            if (fs[i] == lastFileName)
                continue;

            curList.Add(fs[i]);
            if (curList.Count > count)
            {
                curList = new List<string>(count + 1);
                
            }
        }

        // ExecClamScan(fs[i], clamscanFile, clamscanArgs);

        return (int) Errors.success;
    }

    protected static int    clamscanThreads = 0;
    protected static object sync = new Object();
    protected static ConcurrentBag<Process> PIs = new ConcurrentBag<Process>();
    public static void ExecClamScan(List<string> FullNames, string clamscanFile, string clamscanArgs)
    {
        /*
        var firstPath = args[0];
        var psi = new ProcessStartInfo("realpath", firstPath);
        psi.RedirectStandardOutput = true;
        var pi = Process.Start(psi);

        if (pi == null)
        {
            Console.Error.WriteLine($"Execute realpath command failed: {psi.FileName} {psi.Arguments}");
            return 101;
        }

        pi.WaitForExit();
        var reportPath = pi.StandardOutput.ReadToEnd();

        if (!File.Exists(reportPath))
        {
            Console.Error.WriteLine($"Report file not exists: \"{reportPath}\"");
            return 102;
        }*/
    }

    public static void PrintHelp()
    {
        Console.Error.WriteLine("aide_clamav version: " + Version);
        Console.Error.WriteLine("https://github.com/VinnySmallUtilities/aide-clamav");
        Console.Error.WriteLine();
        Console.Error.WriteLine("aide_clamav /path_to_conf_file");
        Console.Error.WriteLine("'aide_clamav ?' for use aide-clamav.conf in current directory");
        Console.Error.WriteLine();
        Console.Error.WriteLine("configuration file format:");
        Console.Error.WriteLine("first line: a path to the AIDE report.log");
        Console.Error.WriteLine("second line: clamscan command: clamscan");
        Console.Error.WriteLine("third line: clamscan arguments");
        Console.Error.WriteLine("fourth line (optional): clamscan processes count for parallel execution");
    }
}
