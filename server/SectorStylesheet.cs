﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Web;
using System.Drawing;

namespace Maps
{
    public class SectorStylesheet
    {
        // Grammar: 
        //   stylesheet       := WS rule-list WS
        //   rule-list        := rule*
        //   rule             := selector-list declaration-list
        //   selector-list    := selector WS ( ',' WS selector )* WS
        //   selector         := element ( '.' code )?
        //   element          := IDENT
        //   code             := IDENT
        //   declaration-list := '{' WS declaration? ( ';' WS declaration? )*  '}' WS
        //   declaration      := property WS ':' WS value WS
        //   property         := IDENT
        //   value            := IDENT | NUMBER | COLOR
        //   IDENT            := [A-Za-z_]([A-Za-z0-9_] | '\' ANY)* 
        //   NUMBER           := '-'? [0-9]* ('.' [0-9]+) ([eE] [-+]? [0-9]+)?
        //   COLOR            := '#' [0-9A-F]{6}
        //   WS               := ( U+0009 | U+000A | U+000D | U+0020 | '/' '*' ... '*' '/')*

        class Rule {
            public Rule(List<Selector> selectors, List<Declaration> declarations) { this.selectors = selectors; this.declarations = declarations; }
            public List<Selector> selectors;
            public List<Declaration> declarations;
        };
        class Selector {
            public Selector(string element, string code) { this.element = element; this.code = code; }
            public string element;
            public string code;

            public override string ToString()
            {
                if (code != null) return element + '.' + code;
                return element;
            }
        }
        class Declaration {
            public Declaration(string property, string value) { this.property = property; this.value = value; }
            public string property;
            public string value;

            public override string ToString()
            {
                return property + ": " + value + ";";
            }
        };

        #region Parser
        [Serializable]
        class ParseException : ApplicationException 
        {
            public ParseException(string message) : base(message) { }
        }
        class Parser
        {
            public Parser(TextReader reader)
            {
                this.reader = reader;
            }
            private TextReader reader;

            public List<Rule> ParseStylesheet()
            {
                WS();
                List<Rule> rules = ParseRuleList();
                WS();
                if (reader.Peek() != -1)
                    throw new ParseException("Expected EOF, saw: " + reader.ReadLine());
                return rules;
            }
            public List<Rule> ParseRuleList()
            {
                List<Rule> rules = new List<Rule>();
                while (true) 
                {
                    Rule rule = ParseRule();
                    if (rule == null) break;
                    rules.Add(rule);
                }
                return rules;
            }
            public Rule ParseRule()
            {
                List<Selector> selectors = ParseSelectorList();
                if (selectors == null) return null;
                List<Declaration> declarations = ParseDeclarationList();
                return new Rule(selectors, declarations);
            }
            public List<Selector> ParseSelectorList()
            {
                Selector selector = ParseSelector();
                if (selector == null) return null;
                List<Selector> selectors = new List<Selector>();
                selectors.Add(selector);
                WS();
                while (reader.Peek() == ',')
                {
                    Expect(',');
                    WS();
                    selector = ParseSelector();
                    if (selector == null) throw new ParseException("Expected selector, saw: " + reader.ReadLine());
                    selectors.Add(selector);
                }
                WS();
                return selectors;
            }
            public Selector ParseSelector()
            {
                string element = IDENT();
                if (element == null) return null;
                string code = null;
                if (reader.Peek() == '.')
                {
                    Expect('.');
                    code = IDENT();
                    if (code == null) throw new ParseException("Expected code, saw: " + reader.ReadLine());
                }
                return new Selector(element, code);
            }
            public List<Declaration> ParseDeclarationList()
            {
                Expect('{');
                WS();
                List<Declaration> declarations = new List<Declaration>();
                Declaration declaration = ParseDeclaration();
                if (declaration != null)
                    declarations.Add(declaration);
                while (reader.Peek() == ';')
                {
                    Expect(';');
                    WS();
                    declaration = ParseDeclaration();
                    if (declaration != null)
                        declarations.Add(declaration);
                }
                Expect('}');
                WS();
                return declarations;
            }
            public Declaration ParseDeclaration()
            {
                string property = IDENT();
                if (property == null) return null;
                WS();
                Expect(':');
                WS();
                string value = ParseValue();
                if (value == null) throw new ParseException("Expected value, saw: " + reader.ReadLine());
                WS();
                return new Declaration(property, value);
            }
            public string ParseValue()
            {
                return IDENT() ?? NUMBER() ?? COLOR();
            }
            public string IDENT()
            {
                int c = reader.Peek();
                if (!(('A' <= c && c <= 'Z') || ('a' <= c && c <= 'z') || c == '_'))
                    return null;
                string ident = Char.ConvertFromUtf32(reader.Read());
                while (true)
                {
                    c = reader.Peek();

                    if (c == '\\')
                    {
                        reader.Read();
                        if (reader.Peek() == -1)
                            throw new ParseException("Expected character after \\, saw EOF");
                    }
                    else if (!(('A' <= c && c <= 'Z') || ('a' <= c && c <= 'z') || ('0' <= c && c <= '9') || c == '_'))
                    {
                        break;
                    }

                    ident += Char.ConvertFromUtf32(reader.Read());
                }
                return ident;
            }
            public string NUMBER()
            {
                int c = reader.Peek();
                if (!(c == '-' || ('0' <= c && c <= '9')))
                    return null;
                string s = "";
                Action consume = () =>
                {
                    s += (char)c;
                    reader.Read();
                    c = reader.Peek();
                };

                if (c == '-')
                    consume();

                if (c < '0' || c > '9')
                    throw new ParseException("Expected digit, saw: " + reader.ReadLine());

                while ('0' <= c && c <= '9')
                    consume();

                if (c == '.')
                {
                    consume();
                    if (c < '0' || c > '9')
                        throw new ParseException("Expected digit, saw: " + reader.ReadLine());
                    while ('0' <= c && c <= '9')
                        consume();
                }

                if (c == 'e' || c == 'E')
                {
                    consume();
                    if (c == '+' || c == '-')
                        consume();
                    if (c < '0' || c > '9')
                        throw new ParseException("Expected digit, saw: " + reader.ReadLine());
                    while ('0' <= c && c <= '9')
                        consume();
                }

                return s;
            }
            public string COLOR()
            {
                int c = reader.Peek();
                if (c != '#')
                    return null;
                reader.Read();
                string s = "#";
                for (int i = 0; i < 6; ++i)
                {
                    c = reader.Peek();
                    if (!('0' <= c && c <= '9') && !('A' <= c && c <= 'F'))
                        throw new ParseException("Expected hex, saw: " + reader.ReadLine());
                    s += (char)reader.Read();
                }
                return s;
            }
            private void WS()
            {
                while (true)
                {
                    switch (reader.Peek())
                    {
                        case 0x09:
                        case 0x0A:
                        case 0x0D:
                        case 0x20:
                            reader.Read();
                            continue;

                        case '/':
                            reader.Read();
                            if (reader.Peek() != '*') throw new ParseException("Expected /* ...*/, saw: " + reader.ReadLine());
                            reader.Read();

                            while (true)
                            {
                                int c = reader.Read();
                                if (c == -1) throw new ParseException("Expected */, saw EOF");
                                if (c != '*') continue;
                                c = reader.Read();
                                if (c == '/') break;
                            }
                            continue;

                        default:
                            return;
                    }
                }
            }
            private void Expect(char c)
            {
                if (reader.Peek() != c) throw new ParseException("Expected '"+c+"', saw: " + reader.ReadLine());
                reader.Read();
            }
        }
        public static SectorStylesheet Parse(string src)
        {
            return Parse(new StringReader(src));
        }
        public static SectorStylesheet Parse(TextReader reader)
        {
            return new SectorStylesheet(new Parser(reader).ParseStylesheet());
        }

        #endregion // Parser

        public SectorStylesheet Parent { get; set; }

        SectorStylesheet(List<Rule> rules)
        {
            this.rules = rules;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var rule in rules)
            {
                bool first = true;
                foreach (var selector in rule.selectors)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(selector.ToString());
                }

                sb.Append(" { ");

                foreach (var declaration in rule.declarations)
                {
                    sb.Append(declaration.ToString());
                    sb.Append(" ");
                }

                sb.Append("}\r\n");
            }
            return sb.ToString();
        }

        public class StyleResult
        {
            public string element;
            public string code;
            public Dictionary<string, string> dict;

            public StyleResult(string element, string code, Dictionary<string, string> dict)
            {
                this.element = element;
                this.code = code;
                this.dict = dict;
            }
            
            private bool GetValue(string property, out string value)
            {
                return dict.TryGetValue(property, out value) && !String.IsNullOrEmpty(value);
            }

            public string GetString(string property)
            {
                string value;
                return GetValue(property, out value) ? value : null;
            }

            public Color? GetColor(string property)
            {
                string value;
                if (!GetValue(property, out value))
                    return null;
                try
                {
                    return ColorTranslator.FromHtml(value);
                }
                catch
                {
                    return null;
                }
            }

            public double? GetNumber(string property)
            {
                string value;
                if (!GetValue(property, out value))
                    return null;
                double result;
                if (Double.TryParse(value, out result))
                    return result;
                return null;
            }

            public T? GetEnum<T>(string property) where T : struct // enum 
            {
                if (!typeof(T).IsEnum)
                    throw new Exception("Type must be an enum");

                string value;
                if (!dict.TryGetValue(property, out value) || String.IsNullOrEmpty(value))
                    return null;

                bool ignoreCase = true;
                T result;
                if (Enum.TryParse<T>(value, ignoreCase, out result))
                    return result;

                return null;
            }
        }

        private Dictionary<Tuple<string, string>, StyleResult> memo = new Dictionary<Tuple<string, string>, StyleResult>();

        private List<SectorStylesheet> Chain()
        {
            var list = new List<SectorStylesheet>();
            var o = this;
            while (o != null)
            {
                list.Insert(0, o);
                o = o.Parent;
            }
            return list;
        }

        public StyleResult Apply(string element, string code)
        {
            var key = Tuple.Create(element, code);
            StyleResult result;
            if (memo.TryGetValue(key, out result))
                return result;

            var dict = new Dictionary<string, Tuple<int, string>>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var sheet in Chain())
            {
                foreach (var rule in sheet.rules)
                {
                    foreach (var selector in rule.selectors)
                    {
                        var match = Match(element, code, selector);
                        if (match == 0)
                            continue;

                        foreach (var declaration in rule.declarations)
                        {
                            Tuple<int, string> current;
                            if (!dict.TryGetValue(declaration.property, out current) || match >= current.Item1)
                                dict[declaration.property] = new Tuple<int, string>(match, declaration.value);
                        }
                    }
                }
            }
            result = new StyleResult(element, code, new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase));
            foreach (var entry in dict)
                result.dict[entry.Key] = entry.Value.Item2;
            memo[key] = result;
            return result;
        }

        private static int Match(string element, string code, Selector selector)
        {
            if (element != selector.element)
                return 0;
            if (selector.code == null)
                return 1;
            if (code != selector.code)
                return 0;
            return 2;
        }

        private List<Rule> rules;
    }
}