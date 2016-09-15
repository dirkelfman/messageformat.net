﻿// MessageFormat for .NET
// - MessageFormatter.cs
// Author: Jeff Hansen <jeff@jeffijoe.com>
// Copyright (C) Jeff Hansen 2014. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Formatting.Formatters;
using Jeffijoe.MessageFormat.Helpers;
using Jeffijoe.MessageFormat.Parsing;

namespace Jeffijoe.MessageFormat
{
    /// <summary>
    ///     The magical Message Formatter.
    /// </summary>
    public class MessageFormatter : IMessageFormatter
    {
        #region Static Fields

        /// <summary>
        ///     The instance of MessageFormatter, with the default locale + cache settings.
        /// </summary>
        private static readonly IMessageFormatter Instance = new MessageFormatter();

        /// <summary>
        ///     The lock object.
        /// </summary>
        private static readonly object Lock = new object();

        #endregion

        #region Fields

        /// <summary>
        ///     Pattern cache. If enabled, should speed up formatting the same pattern multiple times,
        ///     regardless of arguments.
        /// </summary>
        private readonly Dictionary<string, IFormatterRequestCollection> cache;

        /// <summary>
        ///     The formatter library.
        /// </summary>
        private readonly IFormatterLibrary library;

        /// <summary>
        ///     The pattern parser
        /// </summary>
        private readonly IPatternParser patternParser;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageFormatter" /> class.
        /// </summary>
        /// <param name="useCache">
        ///     The use Cache.
        /// </param>
        /// <param name="locale">
        ///     The locale.
        /// </param>
        public MessageFormatter(bool useCache = true, string locale = "en")
            : this(new PatternParser(new LiteralParser()), new FormatterLibrary(), useCache, locale)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageFormatter" /> class.
        /// </summary>
        /// <param name="patternParser">
        ///     The pattern parser.
        /// </param>
        /// <param name="library">
        ///     The library.
        /// </param>
        /// <param name="useCache">
        ///     if set to <c>true</c> uses the cache.
        /// </param>
        /// <param name="locale">
        ///     The locale to use. Formatters may need this.
        /// </param>
        internal MessageFormatter(
            IPatternParser patternParser, 
            IFormatterLibrary library, 
            bool useCache, 
            string locale = "en")
        {
            if (patternParser == null)
            {
                throw new ArgumentNullException("patternParser");
            }

            if (library == null)
            {
                throw new ArgumentNullException("library");
            }

            this.patternParser = patternParser;
            this.library = library;
            this.Locale = locale;
            if (useCache)
            {
                this.cache = new Dictionary<string, IFormatterRequestCollection>();
            }
        }

        #endregion

        #region Public Properties

        /// <summary>
        ///     Gets the formatters library, where you can add your own formatters if you want.
        /// </summary>
        /// <value>
        ///     The formatters.
        /// </value>
        public IFormatterLibrary Formatters
        {
            get
            {
                return this.library;
            }
        }

        /// <summary>
        ///     Gets or sets the locale.
        /// </summary>
        /// <value>
        ///     The locale.
        /// </value>
        public string Locale { get; set; }

        /// <summary>
        ///     Gets the pluralizers dictionary from the <see cref="PluralFormatter" />, if set. Key is the locale.
        /// </summary>
        /// <value>
        ///     The pluralizers, or <c>null</c> if the plural formatter has not been added.
        /// </value>
        public IDictionary<string, Pluralizer> Pluralizers
        {
            get
            {
                var pluralFormatter = this.Formatters.OfType<PluralFormatter>().FirstOrDefault();
                if (pluralFormatter == null)
                {
                    return null;
                }

                return pluralFormatter.Pluralizers;
            }
        }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///     Formats the specified pattern with the specified data.
        /// </summary>
        /// <remarks>
        ///     This method calls <see cref="FormatMessage(string,System.Collections.Generic.Dictionary{string,object})" />
        ///     on a singleton instance using a lock.
        ///     Do not use in a tight loop, as a lock is being used to ensure thread safety.
        /// </remarks>
        /// <param name="pattern">
        ///     The pattern.
        /// </param>
        /// <param name="data">
        ///     The data.
        /// </param>
        /// <returns>
        ///     The formatted message.
        /// </returns>
        public static string Format(string pattern, IDictionary<string, object> data)
        {
            lock (Lock)
            {
                return Instance.FormatMessage(pattern, data);
            }
        }

        /// <summary>
        ///     Formats the specified pattern with the specified data.
        /// </summary>
        /// This method calls
        /// <see cref="FormatMessage(string, object)" />
        /// on a singleton instance using a lock.
        /// Do not use in a tight loop, as a lock is being used to ensure thread safety.
        /// <param name="pattern">
        ///     The pattern.
        /// </param>
        /// <param name="data">
        ///     The data.
        /// </param>
        /// <returns>
        ///     The formatted message.
        /// </returns>
        public static string Format(string pattern, object data)
        {
            lock (Lock)
            {
                return Instance.FormatMessage(pattern, data);
            }
        }

        /// <summary>
        ///     Formats the message with the specified arguments. It's so magical.
        /// </summary>
        /// <param name="pattern">
        ///     The pattern.
        /// </param>
        /// <param name="args">
        ///     The arguments.
        /// </param>
        /// <returns>
        ///     The <see cref="string" />.
        /// </returns>
        public string FormatMessage(string pattern, IDictionary<string, object> args)
        {
            /*
             * We are asuming the formatters are ordered correctly
             * - that is, from left to right, string-wise.
             */
            var sourceBuilder = new StringBuilder(pattern);
            var requests = this.ParseRequests(pattern, sourceBuilder);
            var requestsEnumerated = requests.ToArray();

            // If we got no formatters, then we're done here.
            if (requestsEnumerated.Length == 0)
            {
                return pattern;
            }

            for (int i = 0; i < requestsEnumerated.Length; i++)
            {
                var request = requestsEnumerated[i];

                object value;
                if (args.TryGetValue(request.Variable, out value) == false)
                {
                    value = string.Empty;
                   // throw new VariableNotFoundException(request.Variable);
                }
                
                var formatter = this.Formatters.GetFormatter(request);
                if (formatter == null)
                {
                    throw new FormatterNotFoundException(request);
                }

                // Double dispatch, yeah!
                var result = formatter.Format(this.Locale, request, args, value, this);

                // First, we remove the literal from the source.
                Literal sourceLiteral = request.SourceLiteral;

                // +1 because we want to include the last index.
                var length = (sourceLiteral.EndIndex - sourceLiteral.StartIndex) + 1;
                sourceBuilder.Remove(sourceLiteral.StartIndex, length);

                // Now, we inject the result.
                sourceBuilder.Insert(sourceLiteral.StartIndex, result);

                // The next requests will want to know what happened.
                requests.ShiftIndices(i, result.Length);
            }

            sourceBuilder = this.UnescapeLiterals(sourceBuilder);

            // And we're done.
            return sourceBuilder.ToString();
        }

        /// <summary>
        ///     Formats the message, and uses reflection to create a dictionary of property values from the specified object.
        /// </summary>
        /// <param name="pattern">
        ///     The pattern.
        /// </param>
        /// <param name="args">
        ///     The arguments.
        /// </param>
        /// <returns>
        ///     The <see cref="string" />.
        /// </returns>
        public string FormatMessage(string pattern, object args)
        {
            return this.FormatMessage(pattern, args.ToDictionary());
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Unescapes the literals from the source builder, and returns a new instance with literals unescaped.
        /// </summary>
        /// <param name="sourceBuilder">
        ///     The source builder.
        /// </param>
        /// <returns>
        ///     The <see cref="StringBuilder" />.
        /// </returns>
        protected internal StringBuilder UnescapeLiterals(StringBuilder sourceBuilder)
        {
            // If the block is empty, do nothing.
            if (sourceBuilder.Length == 0)
            {
                return new StringBuilder();
            }

            var dest = new StringBuilder(sourceBuilder.Length, sourceBuilder.Length);
            int length = sourceBuilder.Length;
            const char EscapeChar = '\\';
            const char OpenBrace = '{';
            const char CloseBrace = '}';
            var braceBalance = 0;
            for (int i = 0; i < length; i++)
            {
                var c = sourceBuilder[i];
                if (c == EscapeChar)
                {
                    if (i != length - 1)
                    {
                        char next = sourceBuilder[i + 1];
                        if (next == OpenBrace && braceBalance == 0)
                        {
                            continue;
                        }

                        if (next == CloseBrace && braceBalance == 1)
                        {
                            continue;
                        }
                    }
                }
                else if (c == OpenBrace)
                {
                    braceBalance++;
                }
                else if (c == CloseBrace)
                {
                    braceBalance--;
                }

                dest.Append(c);
            }

            return dest;
        }

        /// <summary>
        ///     Parses the requests, using the cache if enabled and applicable.
        /// </summary>
        /// <param name="pattern">
        ///     The pattern.
        /// </param>
        /// <param name="sourceBuilder">
        ///     The source builder.
        /// </param>
        /// <returns>
        ///     The <see cref="IFormatterRequestCollection" />.
        /// </returns>
        private IFormatterRequestCollection ParseRequests(string pattern, StringBuilder sourceBuilder)
        {
            // If we are not using the cache, just parse them straight away.
            if (this.cache == null)
            {
                return this.patternParser.Parse(sourceBuilder);
            }

            // If we have a cached result from this pattern, clone it and return the clone.
            IFormatterRequestCollection cached;
            if (this.cache.TryGetValue(pattern, out cached))
            {
                return cached.Clone();
            }

            var requests = this.patternParser.Parse(sourceBuilder);
            if (this.cache != null)
            {
                this.cache.Add(pattern, requests.Clone());
            }

            return requests;
        }

        #endregion
    }
}