using GithubSharp.Core.Models.Issues;
using GitIrcBot.Github;
using System;
using System.Collections.Generic;
using System.Text;

namespace GitIrcBot.Github
{
    [Serializable]
    class GithubIssue
    {
        public IssueResponse Issue;
        public List<IssueComment> Comments;

        public GithubIssue()
        {
            Comments = new List<IssueComment>();
        }
    }

    [Serializable]
    public class IssueNotFoundException : System.Exception
    {
    }
}
