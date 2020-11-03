using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WuGanhao.CommandLineParser;
using WuGanhao.GitMirror.GitCommand;

namespace WuGanhao.GitMirror.Command {
    public class GitSyncJob {
        public GitSyncJob(string name, string localFolder, string sourceUrl, /*string branchPattern,*/ string Branch) {
            this.Name          = name;
            this.LocalFolder   = localFolder;
            this.SourceUrl     = sourceUrl;
            //this.BranchPattern = branchPattern;
            this.Branch  = Branch;
        }

        public string Name          { get; }
        public string LocalFolder   { get; }
        public string SourceUrl     { get; }
        //public string BranchPattern { get; }
        public string Branch        { get; }
    }

    public class SyncCommand : SubCommand {

        [CommandOption("git-dir", "d", "Git working directory")]
        public string GirDir { get; set; } = ".";

        [CommandOption("branch", "b", "Branch to sync from source to origin")]
        public string Branch { get; set; }

        [CommandOption("source-url", "u", "Source reposity url to sync to")]
        public string SourceUrl { get; set; }

        [CommandOption("pattern", "p", "Source reposity branches pattern to be discover")]
        public string BranchPattern { get; set; }

        private Regex BRANCH_PATTERN = null;

        /// <summary>
        /// Synchronize
        /// </summary>
        /// <param name="job"></param>
        /// <returns></returns>
        private async IAsyncEnumerable<GitSyncJob> SyncAsync(GitSyncJob job) {
            // Sync current folder
            Repository repo = new Repository(job.LocalFolder);
            Remote origin = repo.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin == null) {
                throw new InvalidOperationException($"Cannot find remote origin for {job.Name}");
            }

            Console.WriteLine($"[{job.Name}] Creating source link => {this.SourceUrl} ");
            Remote source = repo.Remotes.Add("target", job.SourceUrl, true);

            Console.WriteLine($"[{job.Name}] Fetching from source repository...");
            await source.FetchAsync(job.Branch);

            Console.WriteLine($"[{job.Name}] Merging branch: {job.Branch} ...");
            RemoteBranch sourceBranch = source.Branches.FirstOrDefault<RemoteBranch>(b => b.Name == job.Branch);
            if (sourceBranch == null) {
                throw new InvalidOperationException($"[{job.Name}] Failed to find remote branch on target: {job.Branch}");
            }
            await repo.MergeAsync(sourceBranch);

            // Push for unmapped branches
            Console.WriteLine($"[{job.Name}] Create un-mapped branches ...");
            foreach (RemoteBranch branch in
                    source.Branches.Where<RemoteBranch>(b => BRANCH_PATTERN.IsMatch(b.Name))
                    .Where(b => origin.Branches[b.Name] is null)) {
                Console.WriteLine($"[{job.Name}] Fetching {branch} ...");
                await source.FetchAsync(branch.Name);
                Console.WriteLine($"[{job.Name}] Pushing {branch} => origin ...");
                await repo.Push(origin, branch);
            }

            // Check for submodules
            if (repo.Submodules.Configured) {
                foreach (Submodule module in repo.Submodules) {
                    string sourceUrl = module["source-url"];
                    if (string.IsNullOrWhiteSpace(sourceUrl)) {
                        Console.WriteLine($"Skipping for {module.Name}: source-url not yet configured");
                    }

                    await module.InitAsync();
                    yield return new GitSyncJob(module.Name, Path.Combine(job.LocalFolder, module.Path),
                        module["source-url"], module.Branch);
                }
            }
        }

        public async override Task<bool> Run() {

            this.BRANCH_PATTERN = new Regex(this.BranchPattern, RegexOptions.Compiled | RegexOptions.Singleline);

            string rootDir = Path.GetFullPath(this.GirDir);
            string branch = this.Branch.StartsWith("refs/heads/") ? this.Branch.Substring(11) : this.Branch;
            GitSyncJob root = new GitSyncJob("ROOT", rootDir, this.SourceUrl, branch);

            Stack<GitSyncJob> stack = new Stack<GitSyncJob>();
            stack.Push(root);

            GitSyncJob current;
            while ((current = stack.Pop()) != null) {
                await foreach(GitSyncJob subJob in this.SyncAsync(current)) {
                    stack.Push(subJob);
                }
            }

            return true;
        }
    }
}
