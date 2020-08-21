﻿using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.Configuration.Models;
using Umbraco.Core.Configuration.UmbracoSettings;

namespace Umbraco.Core.Composing
{
    /// <summary>
    /// TypeFinder config via appSettings
    /// </summary>
    public class TypeFinderConfig : ITypeFinderConfig
    {
        private readonly TypeFinderSettings _settings;
        private IEnumerable<string> _assembliesAcceptingLoadExceptions;

        public TypeFinderConfig(TypeFinderSettings settings)
        {
            _settings = settings;
        }

        public IEnumerable<string> AssembliesAcceptingLoadExceptions
        {
            get
            {
                if (_assembliesAcceptingLoadExceptions != null)
                    return _assembliesAcceptingLoadExceptions;

                var s = _settings.AssembliesAcceptingLoadExceptions;
                return _assembliesAcceptingLoadExceptions = string.IsNullOrWhiteSpace(s)
                    ? Array.Empty<string>()
                    : s.Split(',').Select(x => x.Trim()).ToArray();
            }
        }
    }
}
