using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace WuGanhao.GitMirror.GitCommand {
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
            await Shell.RunAsync("git", $"remoate add {name} {url}");
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

        public async Task FetchAsync() {
            await this.Repository.FetchAsync(this.Remote.Name, this.Name);
        }
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
                string l = line.Trim();
                string branchName = l.TrimStart('*', ' ');
                yield return new RemoteBranch(this.Repository, this.Remote, branchName);
            }
        }

        IEnumerator<RemoteBranch> IEnumerable<RemoteBranch>.GetEnumerator() {
            foreach(Branch branch in this) {
                yield return (RemoteBranch)branch;
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

        public async Task<int> ShellAsync(string command, string args) =>
            await GitMirror.Shell.RunAsync("git", $"{command} {args}", this.BaseDirectory);

        public async Task<int> ShellAsync(string command, params string[] args) =>
            await this.ShellAsync(command, string.Join(' ', args));

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

        public async void Push (string remote, string refs) => await this.ShellAsync("push", remote, refs);
    }
}
