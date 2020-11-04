using Microsoft.VisualBasic.CompilerServices;
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
        public GitSyncJob(string name, string localFolder, string sourceUrl, string targetUrl, string Branch) {
            this.Name          = name;
            this.LocalFolder   = localFolder;
            this.SourceUrl     = sourceUrl;
            this.TargetUrl     = targetUrl;
            this.Branch        = Branch;
        }

        public string Name          { get; }
        public string LocalFolder   { get; }
        public string SourceUrl     { get; }
        //public string BranchPattern { get; }
        public string Branch        { get; }
        public string TargetUrl     { get; }
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

        [CommandOption("source-token", "t", "Access token for source repository if its from HTTP/HTTPS connection")]
        public string SourceToken { get; set; }

        private Regex BRANCH_PATTERN = null;

        /// <summary>
        /// Synchronize
        /// </summary>
        /// <param name="job"></param>
        /// <returns></returns>
        private async IAsyncEnumerable<GitSyncJob> SyncAsync(GitSyncJob job) {
            string jobName = job.Name ?? "ROOT";
            // Sync current folder
            Repository repo = new Repository(job.LocalFolder);
            Remote origin = repo.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin == null) {
                throw new InvalidOperationException($"[{jobName}] Cannot find remote origin for {job.Name}");
            }

            if (job.TargetUrl != null) {
                origin.FetchUrl = origin.PushUrl = job.TargetUrl;
            }

            Console.WriteLine($"[{jobName}] Creating source link => {job.SourceUrl} ");
            Uri sourceUri = new Uri(job.SourceUrl);
            string schema = sourceUri.Scheme.ToUpperInvariant();
            if (!string.IsNullOrEmpty(this.SourceToken) && (schema == "HTTP" || schema == "HTTPS")) {
                sourceUri = new Uri($"{sourceUri.Scheme}://{this.SourceToken}@{sourceUri.Host}:{sourceUri.Port}{sourceUri.PathAndQuery}");
            }
            Remote source = repo.Remotes.Add("source", sourceUri.ToString(), true);

            Console.WriteLine($"[{jobName}] Fetching from source repository...");
            RemoteBranch sourceBranch = source.Branches[job.Branch];
            if (sourceBranch == null) {
                Console.WriteLine($"[{jobName}] Failed to find branch source: {job.Branch}...");
                yield break;
            }
            await sourceBranch.FetchAsync();

            // for sub-modules, need to check correct branch first.
            if (job.Name != null) {
                RemoteBranch originBranch = origin.Branches[job.Branch];
                if (originBranch != null) { // Could be empty when syncing for first time.
                    await originBranch.FetchAsync();
                    await repo.CheckoutAsync(originBranch, true);

                    Console.WriteLine($"[{jobName}] Merging branch: {job.Branch} ...");
                    if (sourceBranch == null) {
                        throw new InvalidOperationException($"[{jobName}] Failed to find remote branch on target: {job.Branch}");
                    }
                    await repo.MergeAsync(sourceBranch);
                    await repo.PushAsync(origin, job.Branch);
                }
            }

            // Push for unmapped branches
            Console.WriteLine($"[{jobName}] Create un-mapped branches ...");
            RemoteBranch[] sourceBranches = source.Branches.Where<RemoteBranch>(b => BRANCH_PATTERN.IsMatch(b.Name)).ToArray();
            RemoteBranch[] targetBranches = origin.Branches.Where<RemoteBranch>(b => BRANCH_PATTERN.IsMatch(b.Name)).ToArray();
            RemoteBranch[] unmappedBranches = sourceBranches.Where(b => !targetBranches.Any(t => t.Name == b.Name)).ToArray();

            if (unmappedBranches.Length > 0) {
                Console.WriteLine($"[{jobName}] Fetching branches from source: {string.Join(';', unmappedBranches.Select(b => b.Name))} ...");
                await source.FetchAsync(unmappedBranches);
                Console.WriteLine($"[{jobName}] Pushing branches to origin: {string.Join(';', unmappedBranches.Select(b => b.Name))} ...");
                await origin.PushAsync(unmappedBranches);
            }

            // Check for submodules
            if (repo.Submodules.Configured) {
                foreach (Submodule module in repo.Submodules) {
                    string sourceUrl = module["source-url"];
                    if (string.Equals(module["ignore-mirror"], "TRUE", StringComparison.InvariantCultureIgnoreCase)) {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(sourceUrl)) {
                        Console.WriteLine($"[{jobName}] Skipping for {module.Name}: source-url not yet configured");
                        continue;
                    }

                    await module.InitAsync();
                    await module.UpdateAsync();
                    yield return new GitSyncJob(module.Name, Path.Combine(job.LocalFolder, module.Path),
                        module["source-url"], module.Url, module.Branch);
                }
            }
        }

        public async override Task<bool> Run() {

            this.BRANCH_PATTERN = new Regex(this.BranchPattern, RegexOptions.Compiled | RegexOptions.Singleline);

            string rootDir = Path.GetFullPath(this.GirDir);
            string branch = this.Branch.StartsWith("refs/heads/") ? this.Branch.Substring(11) : this.Branch;
            GitSyncJob root = new GitSyncJob(null, rootDir, this.SourceUrl, null, branch);

            Stack<GitSyncJob> stack = new Stack<GitSyncJob>();
            stack.Push(root);

            while (stack.Count > 0) {
                GitSyncJob current = stack.Pop();
                await foreach(GitSyncJob subJob in this.SyncAsync(current)) {
                    stack.Push(subJob);
                }
            }

            return true;
        }
    }
}
