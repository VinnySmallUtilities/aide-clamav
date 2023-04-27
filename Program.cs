﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        var started = DateTime.Now;

        if (args.Length < 1 || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
        {
            PrintHelp();
            return (int)Errors.help;
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
            return (int)Errors.confFileNotExists;
        }

        var confLines = File.ReadAllLines(confFile.FullName);

        if (confFile.Length < 3)
        {
            Console.Error.WriteLine($"Configuration file incorrect: three lines required, but there are {confFile.Length} only");
            PrintHelp();
            return (int)Errors.confFileLengthIncorrect;
        }

        var reportFile = new FileInfo(confLines[0]);
        reportFile.Refresh();

        if (!reportFile.Exists)
        {
            Console.Error.WriteLine($"The AIDE report.log file not exists: \"{reportFile.FullName}\"");
            return (int)Errors.reportFileNotExists;
        }

        var clamscanFile = confLines[1];
        var clamscanArgs = confLines[2];

        if (confLines.Length > 3)
        {
            Int32.TryParse(confLines[3], out clamscanThreads);
            if (clamscanThreads < 1 || clamscanThreads > Environment.ProcessorCount)
            {
                Console.Error.WriteLine("Fourth line in configuration file: clamscanThreads is incorrect. Setted to 1");
            }
        }

        if (clamscanThreads < 1 || clamscanThreads > Environment.ProcessorCount)
        {
            clamscanThreads = Environment.ProcessorCount;
        }

        var reportLines = File.ReadAllLines(reportFile.FullName);
        var fs          = new List<string>(reportLines.Length);
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
                Interlocked.Add(ref TotalSize, fi.Length);
                lock (fs)
                {
                    fs.Add(fi.FullName);
                }
            }
        );

        fs.Sort();
        Console.WriteLine($"Files to scan: " + fs.Count);

        string lastFileName = "";
        var count     = fs.Count / clamscanThreads;
        var curList   = new List<string>(count + 1);
        var lineLen   = 0;
        var FilesSize = 0L;

        File.WriteAllText(reportFileName, "");
        File.WriteAllText(errorFileName,  "");
        for (int i = 0; i < fs.Count; i++)
        {
            // Не повторяем файлы, если вдруг они повторно перечисленны в списке
            if (fs[i] == lastFileName)
                continue;

            curList.Add(fs[i]);
            lineLen   += fs[i].Length;
            FilesSize += new FileInfo(fs[i]).Length;
            if (curList.Count > count || lineLen >= 64*1024 - 4096)
            {
                ExecClamScan(curList, clamscanFile, clamscanArgs, FilesSize);
                curList   = new List<string>(count + 1);
                lineLen   = 0;
                FilesSize = 0;
            }

            PrintExecutionStatus(started, clamscanThreads - 1);
        }

        if (curList.Count > 0)
            ExecClamScan(curList, clamscanFile, clamscanArgs, FilesSize);

        Console.WriteLine($"Started {countOfTasks} tasks. Time {DateTime.Now.ToLocalTime()}");
        PrintExecutionStatus(started);

        if (countOfInfectedFiles > 0)
            Console.WriteLine("FOUND INFECTED FILES: " + countOfInfectedFiles);

        if (fs.Count != countOfScannedFiles)
        {
            Console.WriteLine($"Program ended. FAILURE! Added to scan: {fs.Count} files; scanned: {countOfScannedFiles} files");
        }
        else
        {
            Console.WriteLine($"Program successfully ended. Scanned {countOfScannedFiles} files");
        }

        Console.WriteLine("clamav reports in the file " + Path.GetFullPath(reportFileName));
        if (new FileInfo(errorFileName).Length > 0)
        Console.WriteLine("errors reports in the file " + Path.GetFullPath(errorFileName));

        return (int)Errors.success;

        static void PrintExecutionStatus(DateTime started, int maxCountOfTasks = 0)
        {
            while (countOfTasks > maxCountOfTasks)
            {
                lock (sync)
                    Monitor.Wait(sync, 60_000);

                var now = DateTime.Now;
                var sp = now - started;

                var ct = Console.CursorTop;
                Console.WriteLine($"Executed {countOfTasks} tasks. Execution time {sp.TotalMinutes.ToString("F0")} minutes. Time {now.ToLocalTime()}");
                Console.SetCursorPosition(0, ct);
            }
        }
    }

    public readonly static string reportFileName = "clamav-report.log";
    public readonly static string errorFileName  = "errors.log";

    protected static object sync = new Object();
    protected static int    countOfTasks         = 0;
    protected static int    countOfScannedFiles  = 0;
    protected static int    countOfInfectedFiles = 0;
    protected static long   TotalSize            = 0;
    protected static long   sizeOfScanndeFiles   = 0;

    protected static int    clamscanThreads = 0;
    protected static ConcurrentBag<Process> PIs = new ConcurrentBag<Process>();
    public static void ExecClamScan(List<string> FullNames, string clamscanFile, string clamscanArgs, long FilesSize)
    {
        // Console.WriteLine($"Executed for {FullNames.Count} files");
        Interlocked.Increment(ref countOfTasks);
        ThreadPool.QueueUserWorkItem
        (
            delegate
            {
                var sb = new StringBuilder(1024*1024);

                sb.Append(clamscanArgs);
                foreach (var file in FullNames)
                {
                    sb.Append(" \"");
                    sb.Append(file);
                    sb.Append("\"");
                }
                // File.AppendAllText(reportFileName, sb.ToString());

                var psi = new ProcessStartInfo(clamscanFile, sb.ToString()); sb = null;
                psi.RedirectStandardOutput = true;
                var pi = Process.Start(psi);

                if (pi == null)
                {
                    Console.Error.WriteLine($"Execute command failed: {psi.FileName} {psi.Arguments}");
                    return;
                }

                Process.Start("renice", "-n 19 -p " + pi.Id);
                Process.Start("ionice", "-c 3 -p "  + pi.Id);

                pi.WaitForExit();
                var clamavReport = pi.StandardOutput.ReadToEnd();
                lock (sync)
                    File.AppendAllText(reportFileName, clamavReport + "\n\n----------------------------------------------------------------\n\n");

                int scanned  = 0;
                int infected = 0;
                try
                {
                    infected = getIntValue(clamavReport, "Infected files: ");
                    scanned  = getIntValue(clamavReport, "Scanned files: ");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message);
                }

                Interlocked.Decrement(ref countOfTasks);
                Interlocked.Add(ref countOfScannedFiles,  scanned);
                Interlocked.Add(ref countOfInfectedFiles, scanned);
                Interlocked.Add(ref sizeOfScanndeFiles,   FilesSize);

                lock (sync)
                {
                    Monitor.Pulse(sync);
                }

                if (scanned != FullNames.Count || infected > 0)
                {
                    sb.Clear();
                    sb.AppendLine("Error or infected in files:");
                    sb.AppendJoin("\n", FullNames);

                    lock (sync)
                    File.AppendAllText(errorFileName,  "\n\n----------------------------------------------------------------\n\n\n\n");
                }
            }
        );

        static int getIntValue(string clamavReport, string sfStr)
        {
            int scanned;
            var index0 = clamavReport.IndexOf(sfStr) + sfStr.Length;
            int index1 = -1;
            for (int i = index0; i < clamavReport.Length; i++)
            {
                if (clamavReport[i] >= '0' && clamavReport[i] <= '9')
                {
                    index0 = i;
                    break;
                }
            }

            for (int i = index0; i < clamavReport.Length; i++)
            {
                if (clamavReport[i] >= '0' && clamavReport[i] <= '9')
                    continue;

                index1 = i;
                break;
            }

            var str = clamavReport.Substring(index0, index1 - index0);

            scanned = Int32.Parse(str);
            return scanned;
        }
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
