/**********************************************************************************************************************
 * Copyright Moody's Analytics. All Rights Reserved.
 *
 * This software is the confidential and proprietary information of
 * Moody's Analytics. ("Confidential Information"). You shall not
 * disclose such Confidential Information and shall use it only in
 * accordance with the terms of the license agreement you entered
 * into with Moody's Analytics.
 *********************************************************************************************************************/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace WuGanhao.GitMirror {
    public static class Shell {
        public static void Execute(string command) {
            try {
                ProcessStartInfo procStartInfo = new ProcessStartInfo(command);
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;
                procStartInfo.WindowStyle = ProcessWindowStyle.Normal;
                using Process proc = new Process();
                proc.StartInfo = procStartInfo;
                proc.Start();

                string result = proc.StandardOutput.ReadToEnd();

                Console.WriteLine(result);
            } catch (Exception ex) {
                Console.WriteLine(ex.Message.ToString());
            }
        }

        /// <summary>
        /// Runs a command line and validate its return code and output.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="arguments"></param>
        /// <param name="workingDirectory"></param>
        public static async Task<int> RunAsync(string filename, string arguments, string workingDirectory = null, bool ignoreExitCode = false) {
            if (workingDirectory == null) {
                workingDirectory = Directory.GetCurrentDirectory();
            }

            using Process process = new Process();
            Console.WriteLine($"{workingDirectory}>{filename} {arguments}");

            process.StartInfo.FileName = filename;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (sender, args) => Console.Write(args.Data);
            string error = string.Empty;
            process.ErrorDataReceived += (sender, args) => {
                error += args.Data;
                Console.Error.WriteLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            int exitCode = await Task.FromResult(process.ExitCode);

            // Todo: Need to pass error handling callback method and the caller should decide handling the ExitCode
            if (exitCode != 0 && !ignoreExitCode) {
                throw new SystemException($"Failed executing command line '\"{filename}\" {arguments}' (Exit code: {exitCode})\n" + error);
            }

            return exitCode;
        }

        public static int Run(string filename, string arguments, out string output, string workingDirectory = null, bool ignoreExitCode = false) {
            if (workingDirectory == null) {
                workingDirectory = Directory.GetCurrentDirectory();
            }

            using Process process = new Process();
            string o = string.Empty;
            Console.WriteLine($"{workingDirectory}>{filename} {arguments}");

            process.StartInfo.FileName = filename;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (sender, args) => o += args.Data + Environment.NewLine;
            string error = string.Empty;
            process.ErrorDataReceived += (sender, args) => {
                error += args.Data;
                Console.Error.WriteLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            int exitCode = process.ExitCode;

            // Todo: Need to pass error handling callback method and the caller should decide handling the ExitCode
            if (exitCode != 0 && !ignoreExitCode) {
                throw new SystemException($"Failed executing command line '\"{filename}\" {arguments}' (Exit code: {exitCode})\n" + error);
            }

            output = o;

            return exitCode;
        }

        public static int Run(string filename, string arguments, string workingDirectory = null, bool ignoreExitCode = false) {
            return Run(filename, arguments, out _, workingDirectory, ignoreExitCode);
        }
    }
}
