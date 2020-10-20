using System;
using System.Threading.Tasks;
using WuGanhao.CommandLineParser;
using WuGanhao.GitMirror.Command;

namespace WuGanhao.GitMirror {
    [SubCommand(typeof(SyncCommand), "sync", "Synchronize branch from specified remote repository")]
    public class Program {
        static async Task<int> Main(string[] args) {
            try {
                CommandLineParser<Program> cmdParser = new CommandLineParser<Program>();
                return await cmdParser.Invoke();
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            return 0;
        }
    }
}
