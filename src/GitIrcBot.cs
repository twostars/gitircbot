using GitIrcBot.Properties;
using GitIrcBot.Github;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IrcDotNet;
using IrcDotNet.Samples.Common;
using System.Threading.Tasks;

namespace GitIrcBot
{
    class GitIrcBot : IrcBot
    {
        private GithubWrapper _github;
        public GitIrcBot()
            : base()
        {
            _github = new GithubWrapper();
        }

        public IrcRegistrationInfo RegistrationInfo
        {
            get
            {
                return new IrcUserRegistrationInfo()
                {
                    NickName = Settings.Default.Nick,
                    UserName = Settings.Default.UserName,
                    RealName = "Github Service Bot"
                };
            }
        }

        public override void Run()
        {
            if (!String.IsNullOrEmpty(Settings.Default.Server))
                Connect(Settings.Default.Server, RegistrationInfo);
            else
                Console.WriteLine(Resources.MessageServerNotSupplied);

            base.Run();
        }

        #region Event notifications
        protected override void OnClientRegistered(IrcClient client)
        {
            if (Settings.Default.Channels != null
                && Settings.Default.Channels.Count > 0)
            {
                foreach (var channel in Settings.Default.Channels)
                    client.Channels.Join(channel);
            }
        }

        protected override void OnChannelMessageReceived(IrcChannel channel, IrcMessageEventArgs e)
        {
            var t = Task.Factory.StartNew(() =>
            {
                // Scan for issue numbers.
                var regex = new Regex(@"#(\d+)");
                var matches = regex.Matches(e.Text);
                foreach (Match match in matches)
                {
                    var strIssueNo = match.Value.Replace("#", "");
                    int issueNo = 0;
                    if (!int.TryParse(strIssueNo, out issueNo))
                        continue;

                    try
                    {
                        var issue = _github.LookupIssue(issueNo);
                        var updatedBy = (issue.Issue.Comments == 0 ? issue.Issue.User.Login : issue.Comments[issue.Comments.Count - 1].User.Login);

                        channel.Client.LocalUser.SendMessage(channel,
                            "Issue #{0}, \"{1}\", last updated {2} by {3}. State: {4} (comments: {5}). {6}",
                            issueNo, issue.Issue.Title,
                            ((DateTime)issue.Issue.UpdatedAt).ToRelativeDateUTC(), updatedBy,
                            issue.Issue.State, issue.Issue.Comments,
                            _github.GetIssueUrl(issue));
                    }
                    catch (Exception)
                    {
                        // #menocare
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }

        #endregion

        protected override void InitializeChatCommandProcessors()
        {
            this.ChatCommandProcessors.Add("help", ProcessChatCommandHelp);

            this.ChatCommandProcessors.Add("issue.new", ProcessChatCommandNewIssue);
            this.ChatCommandProcessors.Add("issue.create", ProcessChatCommandNewIssue);
            this.ChatCommandProcessors.Add("newissue", ProcessChatCommandNewIssue);
            this.ChatCommandProcessors.Add("createissue", ProcessChatCommandNewIssue);

            this.ChatCommandProcessors.Add("command.add", ProcessChatCommandAddComment);
            this.ChatCommandProcessors.Add("addcomment", ProcessChatCommandAddComment);

            this.ChatCommandProcessors.Add("command.last", ProcessChatCommandLastComment);
            this.ChatCommandProcessors.Add("lastcomment", ProcessChatCommandLastComment);
        }

        #region Chat Command Processors

        private void ProcessChatCommandHelp(IrcClient client, IIrcMessageSource source,
            IList<IIrcMessageTarget> targets, string command, IList<string> parameters)
        {
            // List all commands recognized by this bot.
            var replyTarget = GetDefaultReplyTarget(client, source, targets);
            client.LocalUser.SendMessage(replyTarget, "Commands recognized by bot:");
            client.LocalUser.SendMessage(replyTarget, string.Join(", ",
                this.ChatCommandProcessors.Select(kvPair => IrcBot.ChatCommandPrefix + kvPair.Key)));
        }

        private void ProcessChatCommandNewIssue(IrcClient client, IIrcMessageSource source,
            IList<IIrcMessageTarget> targets, string command, IList<string> parameters)
        {
            if (targets.Count == 0)
                return;

            var channel = targets[0];
            if (!(channel is IrcChannel))
                return;

            var channelUser = (channel as IrcChannel).GetChannelUser(source as IrcUser);
            var replyTarget = GetDefaultReplyTarget(client, source, targets);

            if (!channelUser.Modes.Contains('o')
                && !channelUser.Modes.Contains('v'))
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: you do not have permission to use this feature.", channelUser.User.NickName);
                return;
            }

            if (parameters.Count == 0)
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: not enough arguments supplied. Example: !newissue \"example title\" \"example message\"", channelUser.User.NickName);
                return;
            }

            var message = string.Join(" ", parameters);

            // Parse the quoted arguments. Example command: !newissue "some title" "some message"
            var args = ParseQuotes(message);
            if (args.Count == 0)
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: not enough arguments supplied. Example: !newissue \"example title\" \"example message\"", channelUser.User.NickName);
                return;
            }

            var issueTitle = string.Format("{0} (from IRC user {1})", args[0], source.Name);
            var issueBody = "(as per title)";
            if (args.Count > 1)
                issueBody = args[1].Replace("|", "\r\n");

            try
            {
                var issue = _github.CreateIssue(issueTitle, issueBody);
                client.LocalUser.SendMessage(replyTarget, "{0}: Issue #{1} created. {2}", 
                    channelUser.User.NickName, issue.Issue.Number, _github.GetIssueUrl(issue));
            }
            catch (Exception ex)
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: Failed to create issue - {1}",
                    channelUser.User.NickName, ex.Message);
            }
        }

        private static List<string> ParseQuotes(string str)
        {
            const char separator = '"';

            var argList = new List<string>();
            int pos = str.IndexOf(separator), oldPos;

            // No quotes at all, use the entire string.
            if (pos < 0)
            {
                argList.Add(str);
                return argList;
            }

            // Assume start is quoted, regardless of whether it is or not.
            oldPos = pos;
            bool bWithinQuote = (str[0] != separator);
            if (bWithinQuote)
                oldPos = 0; 
            
            while ((pos = str.IndexOf(separator, oldPos)) >= 0)
            {
                var substr = str.Substring(oldPos, pos - oldPos).Trim(' ');
                if ((bWithinQuote // add the argument to our list if we're within a quote
                    || str.IndexOf(separator, pos + 1) < 0) // or the user's decided to not use multiple quotes...
                    && substr.Length > 0) // and the potential argument isn't empty.
                    argList.Add(substr);

                bWithinQuote = !bWithinQuote;
                oldPos = pos + 1;
            }

            // If we're still within a quote that hasn't been terminated, 
            // use the rest of the string.
            if (oldPos > 0 && bWithinQuote)
            {
                var substr = str.Substring(oldPos).Trim(' ');
                if (substr.Length > 0)
                    argList.Add(substr);
            }

            return argList;
        }

        private void ProcessChatCommandAddComment(IrcClient client, IIrcMessageSource source,
            IList<IIrcMessageTarget> targets, string command, IList<string> parameters)
        {
            if (targets.Count == 0)
                return;

            var channel = targets[0];
            if (!(channel is IrcChannel))
                return;

            var channelUser = (channel as IrcChannel).GetChannelUser(source as IrcUser);
            var replyTarget = GetDefaultReplyTarget(client, source, targets);
            if (!channelUser.Modes.Contains('o')
                && !channelUser.Modes.Contains('v'))
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: you do not have permission to use this feature.", channelUser.User.NickName);
                return;
            }

            if (parameters.Count < 2)
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: not enough arguments supplied. Example: !addcomment 277 example message", channelUser.User.NickName);
                return;
            }

            int issueNo = 0;
            if (!int.TryParse(parameters[0].Replace("#", ""), out issueNo))
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: {1} is an invalid issue number.", channelUser.User.NickName, parameters[0]);
                return;
            }

            var message = string.Join(" ", (string[]) parameters, 1, parameters.Count - 1);
            var commentBody = string.Format("IRC user {0} writes:\r\n\r\n{1}", 
                source.Name, message);

            try
            {
                var comment = _github.CommentOnIssue(issueNo, commentBody);
                client.LocalUser.SendMessage(replyTarget, "{0}: Comment {1} posted to issue #{2}. {3}",
                    channelUser.User.NickName, comment.Id, issueNo, _github.GetCommentUrl(comment));
            }
            catch (Exception ex)
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: Failed to post comment - {1}",
                    channelUser.User.NickName, ex.Message);
            }
        }

        private void ProcessChatCommandLastComment(IrcClient client, IIrcMessageSource source,
            IList<IIrcMessageTarget> targets, string command, IList<string> parameters)
        {
            if (parameters.Count == 0
                || targets.Count == 0)
                return;

            var channel = targets[0];
            if (!(channel is IrcChannel))
                return;

            var channelUser = (channel as IrcChannel).GetChannelUser(source as IrcUser);
            var replyTarget = GetDefaultReplyTarget(client, source, targets);
            if (!channelUser.Modes.Contains('o')
                && !channelUser.Modes.Contains('v'))
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: you do not have permission to use this feature.", channelUser.User.NickName);
                return;
            }
            
            int issueNo = 0;
            if (!int.TryParse(parameters[0].Replace("#", ""), out issueNo))
            {
                client.LocalUser.SendMessage(replyTarget, "{0}: {1} is an invalid issue number.", channelUser.User.NickName, parameters[0]);
                return;
            }

            try
            {
                const int MaxCharacterLimit = 150;
                var issue = _github.LookupIssue(issueNo);
                string postedBy, post, url;
                DateTime? postedAt;

                if (issue.Issue.Comments == 0)
                {
                    postedBy = issue.Issue.User.Login;
                    postedAt = issue.Issue.CreatedAt;
                    post = issue.Issue.Body;
                    url = _github.GetIssueUrl(issue);

                    FilterMessage(ref post, MaxCharacterLimit);
                    client.LocalUser.SendMessage(replyTarget, "{0} created issue #{1} {2}: {3} ({4})",
                        postedBy, issueNo, ((DateTime)postedAt).ToRelativeDateUTC(), issue.Issue.Title, url);
                }
                else
                {
                    var comment = issue.Comments[issue.Comments.Count - 1];

                    postedBy = comment.User.Login;
                    postedAt = comment.CreatedAt;
                    post = comment.Body;
                    url = _github.GetCommentUrl(comment);

                    FilterMessage(ref post, MaxCharacterLimit);
                    client.LocalUser.SendMessage(replyTarget, "{0} updated issue #{1} {2}: {3} ({4})",
                        postedBy, issueNo, ((DateTime)postedAt).ToRelativeDateUTC(), issue.Issue.Title, url);
                }

                client.LocalUser.SendMessage(replyTarget, "Excerpt: {0}", post);
            }
            catch (IssueNotFoundException)
            {
                client.LocalUser.SendMessage(replyTarget, "Issue #{0} does not exist.", issueNo);
            }
        }

        private bool FilterMessage(ref string message, int limit)
        {
            bool trimmed = false;
            int length;

            // Filter out linebreaks
            message = message.Replace("\r", " ");
            message = message.Replace("\n", " ");

            length = message.Length;
            if (length > limit)
            {
                length = limit;
                trimmed = true;
            }

            // Trim if necessary
            message = message.Substring(0, length);
            if (trimmed)
                message += "...";

            return trimmed;
        }
        #endregion

        protected override void InitializeCommandProcessors()
        {
            this.CommandProcessors.Add("exit", ProcessCommandExit);
            this.CommandProcessors.Add("connect", ProcessCommandConnect);
            this.CommandProcessors.Add("c", ProcessCommandConnect);
            this.CommandProcessors.Add("disconnect", ProcessCommandDisconnect);
            this.CommandProcessors.Add("d", ProcessCommandDisconnect);
            this.CommandProcessors.Add("join", ProcessCommandJoin);
            this.CommandProcessors.Add("j", ProcessCommandJoin);
            this.CommandProcessors.Add("leave", ProcessCommandLeave);
            this.CommandProcessors.Add("l", ProcessCommandLeave);
            this.CommandProcessors.Add("list", ProcessCommandList);
        }

        #region Command Processors

        private void ProcessCommandExit(string command, IList<string> parameters)
        {
            Stop();
        }

        private void ProcessCommandConnect(string command, IList<string> parameters)
        {
            if (parameters.Count < 1)
                throw new ArgumentException(Properties.Resources.MessageNotEnoughArgs);

            Connect(parameters[0], this.RegistrationInfo);
        }

        private void ProcessCommandDisconnect(string command, IList<string> parameters)
        {
            if (parameters.Count < 1)
                throw new ArgumentException(Properties.Resources.MessageNotEnoughArgs);

            Disconnect(parameters[0]);
        }

        private void ProcessCommandJoin(string command, IList<string> parameters)
        {
            if (parameters.Count < 2)
                throw new ArgumentException(Properties.Resources.MessageNotEnoughArgs);

            // Join given channel on given server.
            var client = GetClientFromServerNameMask(parameters[0]);
            var channelName = parameters[1];
            client.Channels.Join(channelName);
        }

        private void ProcessCommandLeave(string command, IList<string> parameters)
        {
            if (parameters.Count < 2)
                throw new ArgumentException(Properties.Resources.MessageNotEnoughArgs);

            // Leave given channel on the given server.
            var client = GetClientFromServerNameMask(parameters[0]);
            var channelName = parameters[1];
            client.Channels.Leave(channelName);
        }

        private void ProcessCommandList(string command, IList<string> parameters)
        {
            // List all active server connections and channels of which local user is currently member.
            foreach (var client in this.Clients)
            {
                Console.Out.WriteLine("Server: {0}", client.ServerName ?? "(unknown)");
                foreach (var channel in client.Channels)
                {
                    if (channel.Users.Any(u => u.User == client.LocalUser))
                    {
                        Console.Out.WriteLine(" * {0}", channel.Name);
                    }
                }
            }
        }

        #endregion
    }
}
