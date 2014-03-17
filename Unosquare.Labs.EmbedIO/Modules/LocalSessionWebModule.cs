﻿namespace Unosquare.Labs.EmbedIO.Modules
{
    using System;
    using System.Linq;
    using System.Net;
    using Unosquare.Labs.EmbedIO;
#if PATCH_COLLECTIONS
    using Unosquare.Labs.EmbedIO.Collections.Concurrent;
#else
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// A simple module to handle in-memory sessions. Do not use for distributed applications
    /// </summary>
    public class LocalSessionWebModule : WebServerModule
    {
        /// <summary>
        /// Defines the session cookie name
        /// </summary>
        public const string SessionCookieName = "__session";

        /// <summary>
        /// The concurrent dictionary holding the sessions
        /// </summary>
        protected ConcurrentDictionary<string, SessionInfo> m_Sessions = new ConcurrentDictionary<string, SessionInfo>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// Creates a session ID, registers the session info in the Sessions collection, and returns the appropriate session cookie.
        /// </summary>
        /// <returns></returns>
        private Cookie CreateSession()
        {
            var sessionId = System.Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                Guid.NewGuid().ToString() + DateTime.Now.Millisecond.ToString() + DateTime.Now.Ticks.ToString()));
            var sessionCookie = new Cookie(SessionCookieName, sessionId);
            this.Sessions[sessionId] = new SessionInfo() { SessionId = sessionId, DateCreated = DateTime.Now, LastActivity = DateTime.Now };
            return sessionCookie;
        }

        /// <summary>
        /// Fixes the session cookie to match the correct value.
        /// System.Net.Cookie.Value only supports a single value and we need to pick the one that potentially exists.
        /// </summary>
        /// <param name="context">The context.</param>
        private void FixupSessionCookie(HttpListenerContext context)
        {
            // get the real "__session" cookie value because sometimes there's more than 1 value and System.Net.Cookie only supports 1 value per cookie
            if (context.Request.Headers["Cookie"] != null)
            {
                var cookieItems = context.Request.Headers["Cookie"].Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var cookieItem in cookieItems)
                {
                    var nameValue = cookieItem.Trim().Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (nameValue.Length == 2 && nameValue[0].Equals(SessionCookieName))
                    {
                        var sessionIdValue = nameValue[1].Trim();
                        if (this.Sessions.ContainsKey(sessionIdValue))
                        {
                            context.Request.Cookies[SessionCookieName].Value = sessionIdValue;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalSessionWebModule"/> class.
        /// </summary>
        public LocalSessionWebModule()
            : base()
        {

            this.Expiration = TimeSpan.FromMinutes(30);

            this.AddHandler(WebServerModuleMap.AnyPath, HttpVerbs.Any, (server, context) =>
            {
                // expire old sessions
                var allKeys = this.Sessions.Keys.ToArray();
                foreach (var key in allKeys)
                {
                    var sessionInfo = this.Sessions[key];
                    if (DateTime.Now.Subtract(sessionInfo.LastActivity) > this.Expiration)
                        this.Sessions.TryRemove(key, out sessionInfo);
                }

                var requestCookie = context.Request.Cookies[SessionCookieName];
                if (requestCookie != null)
                    this.FixupSessionCookie(context);

                if (requestCookie == null)
                {
                    // create the session if sesison not available on the request
                    var sessionCookie = CreateSession();
                    context.Response.SetCookie(sessionCookie);
                    context.Request.Cookies.Add(sessionCookie);
                    server.Log.DebugFormat("Created session identifier '{0}'", sessionCookie.Value);
                }
                else if (requestCookie != null && this.Sessions.ContainsKey(context.Request.Cookies[SessionCookieName].Value) == false)
                {
                    //update session value
                    var sessionCookie = CreateSession();
                    context.Response.SetCookie(sessionCookie); // = sessionCookie.Value;
                    context.Request.Cookies[SessionCookieName].Value = sessionCookie.Value;
                    server.Log.DebugFormat("Updated session identifier to '{0}'", sessionCookie.Value);
                }
                else if (requestCookie != null && this.Sessions.ContainsKey(context.Request.Cookies[SessionCookieName].Value) == true)
                {
                    // If it does exist in the request, check if we're tracking it
                    var requestSessionId = context.Request.Cookies[SessionCookieName].Value;
                    this.Sessions[requestSessionId].LastActivity = DateTime.Now;
                    server.Log.DebugFormat("Session Identified '{0}'", requestSessionId);
                }

                // Always returns false because we need it to handle the rest fo the modules
                return false;
            });

        }

        /// <summary>
        /// The concurrent dictionary holding the sessions
        /// </summary>
        /// <value>
        /// The sessions.
        /// </value>
        public ConcurrentDictionary<string, SessionInfo> Sessions { get { return this.m_Sessions; } }

        /// <summary>
        /// Gets a session object for the given server context.
        /// If no session exists for the context, then null is returned
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns></returns>
        public SessionInfo GetSession(HttpListenerContext context)
        {
            if (context.Request.Cookies[SessionCookieName] == null || Sessions.ContainsKey(context.Request.Cookies[SessionCookieName].Value) == false)
                return null;

            return Sessions[context.Request.Cookies[SessionCookieName].Value];
        }

        /// <summary>
        /// Gets or sets the expiration.
        /// By default, expiration is 30 minutes
        /// </summary>
        /// <value>
        /// The expiration.
        /// </value>
        public TimeSpan Expiration { get; set; }

        /// <summary>
        /// Gets the name of this module.
        /// </summary>
        /// <value>
        /// The name.
        /// </value>
        public override string Name
        {
            get { return "Local Session Module"; }
        }
    }

}