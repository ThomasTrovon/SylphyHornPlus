using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

namespace System
{
    /// <summary>
    /// Compatibility surface used by the bundled MetroRadiance DLL, not a general WCF replacement.
    /// Supports relative resource paths with named variables, including "{theme}.xaml".
    /// </summary>
    public sealed class UriTemplate
    {
        private readonly string template;
        public UriTemplate(string template) { this.template = template ?? throw new ArgumentNullException(nameof(template)); }
        public Uri BindByName(Uri baseAddress, IDictionary<string, string> parameters)
        {
            var path = Regex.Replace(template, @"\{([^{}]+)\}", match =>
            {
                foreach (var pair in parameters)
                    if (string.Equals(pair.Key, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                        return Uri.EscapeDataString(pair.Value);
                throw new ArgumentException("Missing template variable: " + match.Groups[1].Value, nameof(parameters));
            });
            return new Uri(baseAddress, path);
        }
        public UriTemplateMatch Match(Uri baseAddress, Uri candidate)
        {
            if (baseAddress == null || candidate == null || !baseAddress.IsAbsoluteUri || !candidate.IsAbsoluteUri) return null;
            if (!baseAddress.IsBaseOf(candidate)) return null;
            var names = new List<string>();
            var pattern = "^";
            var offset = 0;
            foreach (Match token in Regex.Matches(template, @"\{([^{}]+)\}"))
            {
                pattern += Regex.Escape(template.Substring(offset, token.Index - offset)) + "([^/]+?)";
                names.Add(token.Groups[1].Value);
                offset = token.Index + token.Length;
            }
            pattern += Regex.Escape(template.Substring(offset)) + "$";
            var matched = Regex.Match(baseAddress.MakeRelativeUri(candidate).ToString(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!matched.Success) return null;
            var result = new UriTemplateMatch();
            for (var i = 0; i < names.Count; i++) result.BoundVariables[names[i]] = Uri.UnescapeDataString(matched.Groups[i + 1].Value);
            return result;
        }
    }
    public sealed class UriTemplateMatch
    {
        public NameValueCollection BoundVariables { get; } = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        public object Data { get; set; }
    }
    public sealed class UriTemplateTable
    {
        private readonly Uri baseAddress;
        private readonly TemplateCollection entries = new TemplateCollection();
        public UriTemplateTable(Uri baseAddress) { this.baseAddress = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress)); }
        public IList<KeyValuePair<UriTemplate, object>> KeyValuePairs => entries;
        public void MakeReadOnly(bool allowDuplicateEquivalentUriTemplates) { entries.Locked = true; }
        public UriTemplateMatch MatchSingle(Uri uri)
        {
            UriTemplateMatch result = null;
            foreach (var pair in entries)
            {
                var match = pair.Key.Match(baseAddress, uri);
                if (match == null) continue;
                if (result != null) throw new InvalidOperationException("Multiple resource templates matched.");
                match.Data = pair.Value;
                result = match;
            }
            return result;
        }
        private sealed class TemplateCollection : Collection<KeyValuePair<UriTemplate, object>>
        {
            internal bool Locked;
            private void CheckWritable() { if (Locked) throw new NotSupportedException("The template table is read-only."); }
            protected override void InsertItem(int index, KeyValuePair<UriTemplate, object> item) { CheckWritable(); base.InsertItem(index, item); }
            protected override void SetItem(int index, KeyValuePair<UriTemplate, object> item) { CheckWritable(); base.SetItem(index, item); }
            protected override void RemoveItem(int index) { CheckWritable(); base.RemoveItem(index); }
            protected override void ClearItems() { CheckWritable(); base.ClearItems(); }
        }
    }
}
