using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WuGanhao.GitMirror.GitCommand {

    public class Submodule {
        private Dictionary<string, string> _config;

        public Submodule(Repository repo, string name, string path, string url, string branch, Dictionary<string, string> config) {
            this.Repository = repo;
            this.Name       = name;
            this.Path       = path;
            this.Url        = url;
            this.Branch     = branch;
            this._config    = config;
        }

        public Repository Repository { get; }

        public string Name { get; }
        public string Path { get; }
        public string Url { get; }
        public string Branch { get; }

        public string this[string key] {
            get { return this._config.ContainsKey(key) ? this._config[key] : null; }
        }

        public async Task InitAsync() {
            await this.Repository.ShellAsync("submodule", "init", this.Path);
        }

        public async Task<int> UpdateAsync(bool init = false) =>
            await this.Repository.ShellAsync(true, "submodule", "update", init ? "--init" : null, this.Path);
    }

    public class SubmoduleCollection : IEnumerable<Submodule> {

        public SubmoduleCollection(Repository repo) {
            this.Repository = repo;
        }

        public Repository Repository { get; }

        private static readonly Regex PATTERN_NAME = new Regex("^\\s*\\[submodule\\s+\"(?<name>.+?)\"\\s*\\]\\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex PATTERN_KEYVALUE = new Regex("^\\s*(?<key>.+?)\\s*=\\s*(?<value>.+?)\\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

        public bool Configured => File.Exists(Path.Combine(this.Repository.BaseDirectory, ".gitmodules"));

        public async Task InitAsync() {
            await this.Repository.ShellAsync("init", "--update");
        }

        public IEnumerator<Submodule> GetEnumerator() {
            using FileStream fs = new FileStream(
                Path.Combine(this.Repository.BaseDirectory, ".gitmodules"), FileMode.Open, FileAccess.Read, FileShare.Read);

            string url = null;
            string name = null;
            string branch = null;
            string path = null;
            Dictionary<string, string> config = new Dictionary<string, string>();

            using StreamReader reader = new StreamReader(fs);

            string line;
            while((line = reader.ReadLine()) != null) {
                Match m = PATTERN_NAME.Match(line);
                if (m.Success) {
                    if (name is null) { // First time
                        name = m.Groups["name"].Value;
                    } else {
                        Submodule sm = string.IsNullOrEmpty(name) ? null : new Submodule(this.Repository, name, path, url, branch, config);
                        name = m.Groups["name"].Value;
                        url = path = branch = null;
                        config = new Dictionary<string, string>();
                        yield return sm;
                    }
                    continue;
                }

                m = PATTERN_KEYVALUE.Match(line);
                if (m.Success) {
                    string key   = m.Groups["key"].Value;
                    string value = m.Groups["value"].Value;

                    switch (key) {
                        case "path":
                            path = value;
                            break;

                        case "url":
                            url = value;
                            break;

                        case "branch":
                            branch = value;
                            break;

                        default:
                            config[key] = value;
                            break;
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => throw new System.NotImplementedException();
    }

    public class Remote {
        public Remote(Repository repo, string name) {
            this.Repository = repo;
            this.Name = name;
        }

        public Repository Repository { get; }

        public string Name { get; }

        public string PushUrl {
            get {
                this.Repository.Shell(out string output, "remote", "get-url", "--push", this.Name);
                return output;
            }
            set {
                this.Repository.Shell("remote", "set-url", "--push", this.Name, value);
            }
        }

        public string FetchUrl {
            get {
                this.Repository.Shell(out string output, "remote", "get-url", this.Name);
                return output;
            }
            set {
                this.Repository.Shell("remote", "set-url", this.Name, value);
            }
        }

        private RemoteBranchCollection _branches;
        public RemoteBranchCollection Branches => this._branches ??= new RemoteBranchCollection(this.Repository, this);

        public async Task FetchAsync() {
            await this.Repository.FetchAsync(this.Name);
        }

        public async Task FetchAsync(string refs) {
            await this.Repository.FetchAsync(this.Name, refs);
        }
    }

    public class RemoteCollection: IEnumerable<Remote> {
        internal RemoteCollection(Repository repo) {
            this.Repository = repo;
        }

        public Repository Repository { get; }

        public Remote Add(string name, string url, bool force = false) {
            if (force) {
                Remote remote = this.FirstOrDefault(r => r.Name == name);
                if (remote != null) {
                    remote.PushUrl = remote.FetchUrl = url;
                    return remote;
                }
            }

            return this.AddAsync(name, url).GetAwaiter().GetResult();
        }

        public async Task<Remote> AddAsync(string name, string url) {
            await this.Repository.ShellAsync("remote", "add", name, url);
            return new Remote(this.Repository, name);
        }

        public IEnumerator<Remote> GetEnumerator() {
            this.Repository.Shell(out string remoteNames, "remote");
            string[] names = remoteNames.Split(new char[] { '\n', '\r', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            foreach(string name in names) {
                yield return new Remote(this.Repository, name);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public class Branch {
        public Branch(Repository repo, string branchName, bool active) {
            this.Repository = repo;
            this.Name = branchName;
        }

        public Repository Repository { get; }

        public bool Active { get; }

        public string Name { get; }
    }

    public class RemoteBranch: Branch {
        public RemoteBranch(Repository repo, Remote remote, string branchName)
            : base(repo, branchName, false) {
            this.Remote = remote;
        }

        public Remote Remote { get; }
        public string FullName => $"refs/remotes/{this.Remote.Name}/{this.Name}";

        public async Task FetchAsync() {
            await this.Repository.FetchAsync(this.Remote.Name, this.Name);
        }

        public override string ToString() => $"refs/remotes/{this.Remote.Name}/{this.Name}";
    }

    public class BranchCollection: IEnumerable<Branch> {
        public BranchCollection(Repository repo) {
            this.Repository = repo;
        }

        public Repository Repository { get; }

        public virtual IEnumerator<Branch> GetEnumerator() {
            this.Repository.Shell(out string[] lines, "branch", "-a");
            foreach (string line in lines) {
                string l = line.Trim();
                string branchName = l.TrimStart('*', ' ');
                yield return new Branch(this.Repository, branchName, l.StartsWith('*'));
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    public class RemoteBranchCollection: BranchCollection, IEnumerable<RemoteBranch> {
        public RemoteBranchCollection(Repository repo, Remote remote)
            : base(repo) {
            this.Remote = remote;
        }

        public Remote Remote { get; }

        public override IEnumerator<Branch> GetEnumerator() {
            this.Repository.Shell(out string[] lines, "ls-remote", this.Remote.Name);
            foreach (string line in lines) {
                if (string.IsNullOrEmpty(line)) continue;
                string[] fields = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 2) {
                    continue;
                }
                string branchName = fields[1];
                if (branchName.StartsWith("refs/heads/")) {
                    branchName = branchName.Substring(11);
                }
                yield return new RemoteBranch(this.Repository, this.Remote, branchName);
            }
        }

        IEnumerator<RemoteBranch> IEnumerable<RemoteBranch>.GetEnumerator() {
            foreach(Branch branch in this) {
                yield return (RemoteBranch)branch;
            }
        }

        public RemoteBranch this[string name] {
            get {
                this.Repository.Shell(out string[] lines, "ls-remote", this.Remote.Name, name);
                lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                if (lines.Length <= 0) {
                    return null;
                }

                string branchName = lines.Select(l => {
                    string[] fields = l.Split(new char[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length != 2) {
                        return null;
                    }
                    return fields[1];
                }).Select(l => l.StartsWith("refs/heads/") ? l.Substring(11) : l)
                .Where(n => n == name)
                .FirstOrDefault();

                if (branchName == null) return null;

                return new RemoteBranch(this.Repository, this.Remote, name);
            }
        }
    }

    public class Repository {
        public string BaseDirectory { get; private set; }

        public Repository()
            : this(Directory.GetCurrentDirectory()) {
        }

        public Repository(string baseDir) {
            this.BaseDirectory = baseDir;
        }

        private RemoteCollection _remotes;
        public RemoteCollection Remotes => _remotes ??= new RemoteCollection(this);

        private BranchCollection _branches;
        public BranchCollection Branches => this._branches ??= new BranchCollection(this);

        private SubmoduleCollection _submodules;
        public SubmoduleCollection Submodules => this._submodules ??= new SubmoduleCollection(this);

        public async Task<int> ShellAsync(string command, string args, bool ignoreExitcode = false) =>
            await GitMirror.Shell.RunAsync("git", $"{command} {args}", this.BaseDirectory, ignoreExitcode);

        public async Task<int> ShellAsync(string command, params string[] args) =>
            await this.ShellAsync(command, string.Join(' ', args.Where(a => a != null)));

        public async Task<int> ShellAsync(bool ignoreExitcode, string command, params string[] args) =>
            await this.ShellAsync(command, string.Join(' ', args.Where(a => a != null)), ignoreExitcode);

        public int Shell(out string output, string command, params string[] args) {
            string arguments = string.Join(' ', args);
            return GitMirror.Shell.Run("git", $"{command} {arguments}", out output, this.BaseDirectory);
        }

        public int Shell(out string[] lines, string command, params string[] args) {
            string arguments = string.Join(' ', args);
            return GitMirror.Shell.Run("git", $"{command} {arguments}", out lines, this.BaseDirectory);
        }

        public int Shell(string command, params string[] args) {
            string arguments = string.Join(' ', args);
            return GitMirror.Shell.Run("git", $"{command} {arguments}", this.BaseDirectory);
        }

        public async Task MergeAsync (string refs) => await this.ShellAsync("merge", refs);

        public async Task MergeAsync(RemoteBranch remoteBranch) =>
            await this.ShellAsync("merge", $"remotes/{remoteBranch.Remote.Name}/{remoteBranch.Name}");

        public async Task FetchAsync(string remote, string refs) => await this.ShellAsync("fetch", remote, refs);
        public async Task FetchAsync(string remote) => await this.ShellAsync("fetch", remote);

        public async Task Push (string remote, string refs) => await this.ShellAsync("push", remote, refs);
        public async Task Push (Remote remote, RemoteBranch branch) =>
            await this.ShellAsync("push", remote.Name, $"refs/remotes/{branch.Remote.Name}/{branch.Name}:refs/heads/{branch.Name}");
        public async Task Push(Remote remote, string refs) => await this.ShellAsync("push", remote.Name, refs);
        public async Task CheckoutAsync(RemoteBranch originBranch) =>
            await this.ShellAsync("checkout", originBranch.FullName);
        public async Task CheckoutAsync(string branch, bool orphan = false) =>
            await this.ShellAsync("checkout", orphan ? "--orphan" : null, branch);
    }
}
