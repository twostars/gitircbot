using GitIrcBot.Properties;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using GithubSharp.Core;
using GithubSharp.Core.API;
using GithubSharp.Core.Base;
using GooGlSharp;
using GithubSharp.Core.Models.Issues;

namespace GitIrcBot.Github
{
    class GithubWrapper
    {
        private const int PerPageLimit = 1000;

        private IRequestProxy _requestProxy;
        private AuthenticatedIssuesRepository _issueRepository;

        private string _repositoryName;
        private string _ownerName;
        private string _defaultBranchName;

        private ObjectCache _cache;
        private CacheItemPolicy _cachePolicy;
        private const int CacheExpiresMin = 30;

        private GooGl _urlShortener;

        public GithubWrapper()
        {
            _requestProxy = RequestProxyProvider.OAuth();
            _issueRepository = new AuthenticatedIssuesRepository(_requestProxy);
            _issueRepository.PerPageLimit = PerPageLimit;

            _repositoryName = Settings.Default.GithubRepositoryName;
            _ownerName = Settings.Default.GithubOwnerName;
            _defaultBranchName = Settings.Default.GithubDefaultBranch;

            _cache = MemoryCache.Default;
            _cachePolicy = new CacheItemPolicy();
            _cachePolicy.AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(CacheExpiresMin);

            _urlShortener = new GooGl();
        }

        private string GetCacheKey(string format, params object[] args)
        {
            return string.Format("{0}/{1}/{2}/{3}", _ownerName, _repositoryName, _defaultBranchName, string.Format(format, args));
        }

        private string GetIssueKey(int issueID)
        {
            return GetCacheKey("issue/{0}", issueID);
        }

        public GithubIssue LookupIssue(int issueID)
        {
            try
            {
                var key = GetIssueKey(issueID);
                if (_cache.Contains(key))
                    return _cache[key] as GithubIssue;

                var issue = new GithubIssue();

                // If the issue does not exist, it will return a 404 exception which will be caught.
                issue.Issue = _issueRepository.View(_repositoryName, _ownerName, issueID);

                // Retrieve comments, if necessary.
                if (issue.Issue.Comments > 0)
                {
                    foreach (var comment in _issueRepository.Comments(_repositoryName, _ownerName, issueID))
                        issue.Comments.Add(comment);
                }

                _cache.Set(key, issue, _cachePolicy);
                return issue;
            }
            catch (WebException ex)
            {
                ConvertThrown404(ex);
            }

            return null;
        }

        public GithubIssue CreateIssue(string title, string body)
        {
            try
            {
                // Attempt to create a new issue
                var request = new CreateIssueRequest(title, body);
                var issue = new GithubIssue();

                issue.Issue = _issueRepository.Create(_repositoryName, _ownerName, request);

                return issue; // note: do not cache issue, comments are more likely to be made in the interim.
            }
            catch (WebException ex)
            {
                ConvertThrown404(ex);
            }

            return null;
        }

        public IssueComment CommentOnIssue(int issueID, string message)
        {
            try
            {
                // Attempt to post a comment
                var comment = _issueRepository.CommentOnIssue(_repositoryName, _ownerName, issueID, message);

                // If this issue is cached, update the cache data.
                var key = GetIssueKey(issueID);
                if (_cache.Contains(key))
                {
                    var issue = _cache[key] as GithubIssue;

                    issue.Issue.Comments++;
                    issue.Comments.Add(comment);
                }

                return comment;
            }
            catch (WebException ex)
            {
                ConvertThrown404(ex);
            }

            return null;
        }

        private void ConvertThrown404(WebException ex)
        {
            // Convert 404s into IssueNotFoundExceptions to make handling easier.
            if (ex.Status == WebExceptionStatus.ProtocolError
                && ex.Response != null
                && ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.NotFound)
                throw new IssueNotFoundException();

            throw ex;
        }

        /**
         * @brief   Requests the goo.gl shortened URL.
         *          If request could not be made, returns the original url.
         *
         * @param   url URL of the document.
         *
         * @return  The shortened URL. On error, the original url.
         */
        private string GetShortenedUrl(string url)
        {
            try
            {
                if (String.IsNullOrEmpty(Settings.Default.GooGlAPIKey))
                    return url;

                return _urlShortener.Shorten(new Uri(url), Settings.Default.GooGlAPIKey).ToString();
            }
            catch (Exception)
            {
                return url;
            }
        }

        public string GetIssueUrl(GithubIssue issue)
        {
            return GetShortenedUrl(issue.Issue.HtmlUrl);
        }

        public string GetCommentUrl(IssueComment comment)
        {
            return GetShortenedUrl(comment.HtmlUrl);
        }
    }
}
