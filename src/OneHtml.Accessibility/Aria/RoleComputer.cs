using System;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>
/// Computes the role of every element of a document from markup alone: the explicit role (WAI-ARIA 1.2 section 8.1),
/// the implicit role (HTML-AAM section 3.5), presentational conflict resolution, inherited presentational for required
/// owned elements, and presentational children, with diagnostics about the role and the WAI-ARIA attributes.
/// </summary>
public static class RoleComputer
{
    /// <summary>
    /// Computes roles for all elements of <paramref name="document"/>. Time is linear in the size of the document and
    /// the traversal uses no recursion, so depth is not limited by the stack.
    /// </summary>
    /// <param name="document">A parsed document.</param>
    /// <param name="options">Limits and the optional name probe; defaults when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <returns>The map; template content is not visited.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static RoleMap Compute(Document document, RoleOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Pass(document, options ?? new RoleOptions(), cancellationToken).Run();
    }

    /// <summary>The role name as Core-AAM's computed role gives it; see <see cref="NodeRole.ComputedRole"/>.</summary>
    /// <param name="role">A role.</param>
    public static string ComputedRoleName(AriaRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.Name switch
        {
            "presentation" => "none",
            "img" => "image",
            "directory" => "list",
            _ => role.Name,
        };
    }

    /// <summary>
    /// Resolves the value of a <c>role</c> attribute: the first token that names a valid, non-abstract role, compared
    /// ASCII case-insensitively, with <c>image</c> as a synonym of <c>img</c> (WAI-ARIA 1.3 draft) and the whitespace
    /// of the HTML Standard as separator.
    /// </summary>
    /// <param name="roleAttribute">The attribute value.</param>
    /// <returns>The role, or <see langword="null"/> when no token is usable.</returns>
    public static AriaRole? ResolveExplicitRole(ReadOnlySpan<char> roleAttribute) =>
        ResolveExplicit(roleAttribute, 1024, null, out _, null);

    /// <param name="value">The role attribute value.</param>
    /// <param name="maxTokens">The most tokens examined.</param>
    /// <param name="diagnostics">Receives invalid and abstract tokens that precede the chosen role; may be null.</param>
    /// <param name="truncated">Set when the token limit stopped the scan.</param>
    /// <param name="named">
    /// Called when a token is <c>region</c> or <c>form</c>, which authors must name (WAI-ARIA 1.2 sections 5.4 and 9.1,
    /// "name from author"): an unnamed one is skipped like an unknown token. <see langword="null"/> accepts them.
    /// </param>
    internal static AriaRole? ResolveExplicit(ReadOnlySpan<char> value, int maxTokens, List<RoleDiagnostic>? diagnostics, out bool truncated, Func<bool>? named)
    {
        truncated = false;
        int pos = 0;
        int count = 0;
        while (AsciiText.NextToken(value, ref pos, out ReadOnlySpan<char> token))
        {
            if (++count > maxTokens)
            {
                truncated = true;
                return null;
            }

            AriaRole? role = AriaTaxonomy.FindRole(token);
            if (role is null && Ascii.EqualsIgnoreCase(token, "image"))
            {
                // WAI-ARIA 1.3 draft: image is the preferred synonym of img.
                role = AriaTaxonomy.FindRole("img");
            }

            if (role is null)
            {
                diagnostics?.Add(new RoleDiagnostic(RoleDiagnosticKind.InvalidRoleToken, "role", Clip(token), "Not a WAI-ARIA role."));
                continue;
            }

            if (role.IsAbstract)
            {
                diagnostics?.Add(new RoleDiagnostic(RoleDiagnosticKind.AbstractRole, "role", Clip(token), "Abstract roles must not be used."));
                continue;
            }

            if (named is not null && role.Name is "region" or "form" && !named())
            {
                continue;
            }

            return role;
        }

        return null;
    }

    internal static string Clip(ReadOnlySpan<char> s) => s.Length <= 64 ? s.ToString() : string.Concat(s[..64], "...");

    /// <summary>Per-element state handed to children during the traversal.</summary>
    private struct Frame
    {
        public bool Inert;
        public bool DisabledByFieldset;
        public bool LegendPending; // a disabled fieldset whose first legend child has not been seen
        public bool IsDetails;
        public bool SummarySeen;
        public bool ChildrenPresentational;
        public bool TableIsGrid;
        public bool RowHasDataCell;
        public bool InList; // the nearest ancestor that is neither generic nor presentational has the list role
        public LandmarkScope Scope;
        public string[]? OwnedRoles;
    }

    private sealed class Pass
    {
        private readonly Document _doc;
        private readonly RoleOptions _options;
        private readonly CancellationToken _ct;
        private readonly int _n;
        private readonly AriaRole?[] _role;
        private readonly AriaRole?[] _explicit;
        private readonly AriaRole?[] _implicit;
        private readonly AriaRole?[] _whenNamed;
        private readonly byte[] _origin;
        private readonly byte[] _flags;
        private readonly int[] _diagStart;
        private readonly int[] _diagCount;
        private readonly List<RoleDiagnostic> _allDiagnostics = new();
        private readonly List<RoleDiagnostic> _scratch = new();
        private Frame[] _stack = new Frame[64];

        public Pass(Document doc, RoleOptions options, CancellationToken ct)
        {
            _doc = doc;
            _options = options;
            _ct = ct;
            _n = doc.NodeCount;
            _role = new AriaRole?[_n];
            _explicit = new AriaRole?[_n];
            _implicit = new AriaRole?[_n];
            _whenNamed = new AriaRole?[_n];
            _origin = new byte[_n];
            _flags = new byte[_n];
            _diagStart = new int[_n];
            _diagCount = new int[_n];
        }

        public RoleMap Run()
        {
            // Explicit traversal: stack[d] is the state that applies to the children of the element at depth d.
            _stack[0] = default;
            int depth = 0;
            Node cur = _doc.Root.FirstChild;
            int visited = 0;
            while (!cur.IsNull)
            {
                if ((++visited & 1023) == 0)
                {
                    _ct.ThrowIfCancellationRequested();
                }

                bool descend = false;
                if (cur.IsElement)
                {
                    if (depth + 2 >= _stack.Length)
                    {
                        Array.Resize(ref _stack, _stack.Length * 2);
                    }

                    ProcessElement(cur, ref _stack[depth], out _stack[depth + 1]);
                    descend = !cur.FirstChild.IsNull;
                }

                if (descend)
                {
                    depth++;
                    cur = cur.FirstChild;
                    continue;
                }

                // Advance to the next sibling, climbing while there is none.
                while (!cur.IsNull)
                {
                    Node next = cur.NextSibling;
                    if (!next.IsNull)
                    {
                        cur = next;
                        break;
                    }

                    if (depth == 0)
                    {
                        cur = default;
                        break;
                    }

                    depth--;
                    cur = cur.Parent;
                }
            }

            return new RoleMap(_doc, _role, _explicit, _implicit, _whenNamed, _origin, _flags, _diagStart, _diagCount, _allDiagnostics.ToArray());
        }

        private void ProcessElement(Node n, ref Frame parent, out Frame child)
        {
            int i = n.Index;
            bool html = n.Namespace == ElementNamespace.Html;
            string local = n.LocalName;
            child = default;
            _scratch.Clear();

            // One pass over the attributes for everything the role computation reads.
            ReadOnlySpan<char> roleAttr = default;
            bool hasRole = false;
            bool hasTabindex = false;
            bool tabindexValid = false;
            bool hasInert = false;
            bool hasDisabledAttr = false;
            bool editable = false;
            bool hasGlobalAria = false;
            AttributeList attrs = n.Attributes;
            for (int a = 0; a < attrs.Count; a++)
            {
                Attr attr = attrs[a];
                if (attr.Namespace != AttributeNamespace.None)
                {
                    continue;
                }

                string name = attr.LocalName;
                if (name.Length >= 6 && Ascii.EqualsIgnoreCase(name.AsSpan(0, 5), "aria-"))
                {
                    AriaProperty? p = AriaTaxonomy.FindProperty(name);
                    if (p is { IsGlobal: true })
                    {
                        hasGlobalAria = true;
                    }

                    continue;
                }

                if (Ascii.EqualsIgnoreCase(name, "role"))
                {
                    roleAttr = attr.Value;
                    hasRole = true;
                }
                else if (Ascii.EqualsIgnoreCase(name, "tabindex"))
                {
                    hasTabindex = true;
                    tabindexValid = HtmlSemantics.TryParseInteger(attr.Value, out _);
                }
                else if (Ascii.EqualsIgnoreCase(name, "inert"))
                {
                    hasInert = true;
                }
                else if (Ascii.EqualsIgnoreCase(name, "disabled"))
                {
                    hasDisabledAttr = true;
                }
                else if (Ascii.EqualsIgnoreCase(name, "contenteditable"))
                {
                    ReadOnlySpan<char> v = attr.Value;
                    editable = v.IsEmpty || HtmlSemantics.EqualsToken(v, "true") || HtmlSemantics.EqualsToken(v, "plaintext-only");
                }
            }

            // ---- explicit role (WAI-ARIA 1.2 section 8.1) ----
            AriaRole? explicitRole = null;
            bool truncatedTokens = false;
            if (hasRole)
            {
                Func<Node, bool>? probe = _options.HasAccessibleName;
                explicitRole = ResolveExplicit(roleAttr, _options.MaxRoleTokens, _scratch, out truncatedTokens, probe is null ? null : () => probe(n));
            }

            // ---- context for the HTML mapping ----
            bool isFirstSummary = false;
            if (html && local == "summary" && parent.IsDetails)
            {
                isFirstSummary = !parent.SummarySeen;
                parent.SummarySeen = true;
            }

            // A disabled fieldset's first legend child is exempt from the fieldset's disabled state (HTML 4.10.18.5).
            bool disabledByFieldset = parent.DisabledByFieldset;
            bool childLegendExempt = false;
            if (html && local == "legend" && parent.LegendPending)
            {
                parent.LegendPending = false;
                childLegendExempt = true;
            }

            var ctx = new MapContext(parent.Scope, parent.TableIsGrid, isFirstSummary, parent.RowHasDataCell, parent.InList);
            ImplicitRole implicitRole = HtmlRoleMapper.Map(n, ctx);
            AriaRole? implicitResolved = implicitRole.Role;
            AriaRole? whenNamed = null;
            if (implicitRole.RoleWhenNamed is not null)
            {
                if (_options.HasAccessibleName is { } probe)
                {
                    if (probe(n))
                    {
                        implicitResolved = implicitRole.RoleWhenNamed;
                    }
                }
                else
                {
                    whenNamed = implicitRole.RoleWhenNamed;
                }
            }

            // ---- static state ----
            bool inert = parent.Inert || hasInert;
            bool disabled = false;
            if (html)
            {
                if (local is "button" or "input" or "select" or "textarea")
                {
                    disabled = hasDisabledAttr || (disabledByFieldset && !childLegendExempt);
                }
                else if (local is "fieldset" or "optgroup" or "option")
                {
                    disabled = hasDisabledAttr;
                }
            }

            bool focusable = !inert && IsFocusable(n, html, local, hasTabindex && tabindexValid, editable, disabled, isFirstSummary);

            // ---- role resolution ----
            AriaRole? role;
            RoleOrigin origin;
            string? implicitName = implicitResolved?.Name;
            bool explicitPresentational = explicitRole is { Name: "none" or "presentation" };
            if (explicitRole is not null && !explicitPresentational)
            {
                role = explicitRole;
                origin = RoleOrigin.Explicit;
            }
            else if (explicitPresentational)
            {
                if (focusable || hasGlobalAria)
                {
                    // WAI-ARIA 1.2, Presentational Roles Conflict Resolution: the role is ignored.
                    role = implicitResolved;
                    origin = role is null ? RoleOrigin.None : RoleOrigin.ConflictResolved;
                }
                else
                {
                    role = explicitRole;
                    origin = RoleOrigin.Explicit;
                }
            }
            else if (implicitName is not null && parent.OwnedRoles is { } owned && Array.IndexOf(owned, implicitName) >= 0 && !focusable)
            {
                // WAI-ARIA 1.2 section 5.2.6: required owned elements of a presentational element are presentational.
                role = AriaTaxonomy.FindRole("none");
                origin = RoleOrigin.InheritedPresentational;
            }
            else
            {
                role = implicitResolved;
                origin = role is null ? RoleOrigin.None : RoleOrigin.Implicit;
            }

            // The role the attribute checks use is the element's own, not one imposed by an ancestor.
            AriaRole? ownRole = origin == RoleOrigin.InheritedPresentational ? implicitResolved : role;

            if (parent.ChildrenPresentational && !focusable)
            {
                // WAI-ARIA 1.2 section 5.2.9: descendants of a role with presentational children are presentational.
                role = AriaTaxonomy.FindRole("none");
                origin = RoleOrigin.ChildrenPresentational;
            }

            // ---- state for children ----
            child.Inert = inert;
            child.DisabledByFieldset = disabledByFieldset && !childLegendExempt;
            child.Scope = parent.Scope;
            child.TableIsGrid = parent.TableIsGrid;
            if (html)
            {
                switch (local)
                {
                    case "article":
                    case "aside":
                    case "nav":
                    case "section":
                        child.Scope = LandmarkScope.Sectioning;
                        break;
                    case "main":
                        child.Scope = LandmarkScope.Main;
                        break;
                    case "body":
                        child.Scope = LandmarkScope.Body;
                        break;
                    case "table":
                        child.TableIsGrid = explicitRole is { Name: "grid" or "treegrid" };
                        break;
                    case "details":
                        child.IsDetails = true;
                        break;
                    case "fieldset":
                        if (hasDisabledAttr)
                        {
                            child.DisabledByFieldset = true;
                            child.LegendPending = true;
                        }

                        break;
                    case "tr":
                        child.RowHasDataCell = RowHasDataCell(n);
                        break;
                }
            }

            // A presentational element still passes its native role to its owned elements (li under ul role=none).
            AriaRole? listBasis = origin == RoleOrigin.Explicit && explicitPresentational ? implicitResolved : role;
            child.InList = listBasis is null || listBasis.Name is "generic" or "none" or "presentation" ? parent.InList : listBasis.Name == "list";
            child.ChildrenPresentational = parent.ChildrenPresentational || (origin != RoleOrigin.ChildrenPresentational && role is { ChildrenPresentational: true });
            if (origin == RoleOrigin.InheritedPresentational)
            {
                child.OwnedRoles = parent.OwnedRoles;
            }
            else if (origin == RoleOrigin.Explicit && explicitPresentational && implicitName is not null)
            {
                IReadOnlyList<string> closure = AriaTaxonomy.GetRequiredOwnedClosure(implicitName);
                child.OwnedRoles = closure.Count == 0 ? null : (closure as string[] ?? [.. closure]);
            }

            // ---- diagnostics ----
            if (truncatedTokens)
            {
                _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.LimitExceeded, "role", null, "Only the first role tokens were examined."));
            }

            CheckAttributes(n, attrs, ownRole, origin == RoleOrigin.Explicit && !explicitPresentational ? explicitRole : null, implicitResolved, focusable);

            int max = Math.Max(1, _options.MaxDiagnosticsPerElement);
            int keep = Math.Min(_scratch.Count, max);
            _diagStart[i] = _allDiagnostics.Count;
            if (_scratch.Count > max)
            {
                _allDiagnostics.AddRange(_scratch.GetRange(0, max - 1));
                _allDiagnostics.Add(new RoleDiagnostic(RoleDiagnosticKind.LimitExceeded, null, null, "More diagnostics were found than the limit keeps."));
                keep = max;
            }
            else
            {
                _allDiagnostics.AddRange(_scratch);
            }

            _diagCount[i] = keep;

            _role[i] = role;
            _explicit[i] = explicitRole;
            _implicit[i] = implicitResolved;
            _whenNamed[i] = whenNamed;
            _origin[i] = (byte)origin;
            _flags[i] = (byte)(RoleMap.FlagComputed | (focusable ? RoleMap.FlagFocusable : 0) | (disabled ? RoleMap.FlagDisabled : 0) | (inert ? RoleMap.FlagInert : 0));
        }

        private static bool RowHasDataCell(Node tr)
        {
            for (Node c = tr.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (HtmlSemantics.IsHtml(c, "td"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>HTML 6.6.3 "focusable area", without style: what the markup alone makes focusable.</summary>
        private static bool IsFocusable(Node n, bool html, string local, bool validTabindex, bool editable, bool disabled, bool isFirstSummary)
        {
            if (!html)
            {
                return validTabindex;
            }

            switch (local)
            {
                case "button":
                case "select":
                case "textarea":
                    return !disabled;
                case "input":
                    return !disabled && HtmlSemantics.GetInputType(n) != HtmlInputType.Hidden;
                case "a":
                case "area":
                    return validTabindex || HtmlSemantics.Has(n, "href");
                case "iframe":
                    return true;
                case "summary":
                    return validTabindex || isFirstSummary;
                case "audio":
                case "video":
                    return validTabindex || HtmlSemantics.Has(n, "controls");
                case "fieldset":
                case "optgroup":
                case "option":
                    return validTabindex && !disabled;
                default:
                    return validTabindex || editable;
            }
        }

        private void CheckAttributes(Node n, AttributeList attrs, AriaRole? role, AriaRole? requiredCheckRole, AriaRole? implicitRole, bool focusable)
        {
            // Presentational data: `none` shares the characteristics of `presentation`.
            AriaRole? data = role is { Name: "none" } ? AriaTaxonomy.FindRole("presentation") : role;
            int limit = Math.Max(1, _options.MaxDiagnosticsPerElement) + 1;
            for (int a = 0; a < attrs.Count && _scratch.Count < limit; a++)
            {
                Attr attr = attrs[a];
                string name = attr.LocalName;
                if (attr.Namespace != AttributeNamespace.None || name.Length < 6 || !Ascii.EqualsIgnoreCase(name.AsSpan(0, 5), "aria-"))
                {
                    continue;
                }

                AriaProperty? prop = AriaTaxonomy.FindProperty(name);
                if (prop is null)
                {
                    _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.UnknownAttribute, name, null, "Not a WAI-ARIA state or property."));
                    continue;
                }

                if (data is not null)
                {
                    if (data.Prohibited.Contains(name) || (prop.IsGlobal && ContainsIgnoreCase(prop.GlobalExceptRoles, data.Name)))
                    {
                        _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.ProhibitedAttribute, name, null, "The " + data.Name + " role prohibits this attribute."));
                    }
                    else if (!prop.IsGlobal && !data.Supported.Contains(name))
                    {
                        _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.UnsupportedAttribute, name, null, "The " + data.Name + " role does not support this attribute."));
                    }
                    else if (data.Deprecated.Contains(name) || prop.IsDeprecated)
                    {
                        _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.DeprecatedAttribute, name, null, "Deprecated on the " + data.Name + " role."));
                    }
                }
                else if (prop.IsDeprecated)
                {
                    _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.DeprecatedAttribute, name, null, "Deprecated."));
                }

                AttributeValueChecker.Check(_doc, prop, name, attr.Value, _options.MaxIdReferencesPerAttribute, _scratch);
            }

            // Required states and properties apply to explicit roles; a native element supplies its own (ARIA in
            // HTML: the host language's state satisfies them).
            if (requiredCheckRole is not null && !string.Equals(requiredCheckRole.Name, implicitRole?.Name, StringComparison.Ordinal))
            {
                CheckRequired(n, requiredCheckRole, attrs, focusable);
            }
        }

        private void CheckRequired(Node n, AriaRole role, AttributeList attrs, bool focusable)
        {
            foreach (string req in role.RequiredAttributes)
            {
                if (!Present(attrs, req) && !NativelySupplied(n, req))
                {
                    _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.MissingRequiredAttribute, req, null, "The " + role.Name + " role requires this attribute."));
                }
            }

            if (focusable)
            {
                foreach (string req in role.RequiredWhenFocusable)
                {
                    if (!Present(attrs, req) && !NativelySupplied(n, req))
                    {
                        _scratch.Add(new RoleDiagnostic(RoleDiagnosticKind.MissingRequiredAttribute, req, null, "A focusable " + role.Name + " requires this attribute."));
                    }
                }
            }
        }

        private static bool Present(AttributeList attrs, string name)
        {
            for (int a = 0; a < attrs.Count; a++)
            {
                Attr attr = attrs[a];
                if (attr.Namespace == AttributeNamespace.None && Ascii.EqualsIgnoreCase(attr.LocalName, name))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>HTML-AAM section 3.6: attributes that map to the state or property, so the author need not repeat it.</summary>
        private static bool NativelySupplied(Node n, string ariaName)
        {
            if (!HtmlSemantics.IsHtml(n))
            {
                return false;
            }

            string local = n.LocalName;
            switch (ariaName)
            {
                case "aria-level":
                    return local is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
                case "aria-checked":
                    return HtmlSemantics.GetInputType(n) is HtmlInputType.Checkbox or HtmlInputType.Radio;
                case "aria-selected":
                    return local == "option";
                case "aria-valuenow":
                    return local is "meter" or "progress" || HtmlSemantics.GetInputType(n) is HtmlInputType.Range or HtmlInputType.Number;
                case "aria-expanded":
                case "aria-controls":
                    return local == "select" || HtmlSemantics.GetInputType(n) is HtmlInputType.Text or HtmlInputType.Search or HtmlInputType.Telephone or HtmlInputType.Url or HtmlInputType.Email;
                default:
                    return false;
            }
        }

        private static bool ContainsIgnoreCase(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Ascii.EqualsIgnoreCase(list[i], value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
