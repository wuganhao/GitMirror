using System;
using System.Linq;
using System.Threading.Tasks;
using WuGanhao.CommandLineParser;
using WuGanhao.GitMirror.GitCommand;

namespace WuGanhao.GitMirror.Command {
    public class SyncCommand : SubCommand {

        [CommandOption("git-dir", "d", "Git working directory")]
        public string GirDir { get; set; } = ".";

        [CommandOption("source-branch", "b", "Source repository branch to sync from")]
        public string SourceBranch { get; set; }

        [CommandOption("source-url", "u", "Source reposity url to sync to")]
        public string SourceUrl { get; set; }

        public async override Task<bool> Run() {
            Repository repo = new Repository(this.GirDir);

            Remote origin = repo.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin == null) {
                throw new InvalidOperationException("Cannot find remote: origin");
            }

            Console.WriteLine("Creating source link...");
            Remote source = repo.Remotes.Add("target", this.SourceUrl, true);
            if (source == null) {
                throw new InvalidOperationException("Failed to create target remote");
            }

            Console.WriteLine("Fetching from source repository...");
            await source.FetchAsync(this.SourceBranch);
            await repo.MergeAsync($"refs/{source.Name}/{this.SourceBranch}");

            return true;
        }
    }
}
