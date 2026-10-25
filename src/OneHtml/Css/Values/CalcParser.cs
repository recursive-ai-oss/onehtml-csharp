using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// Parses <c>calc()</c> and the other math functions of CSS Values 4 §10 with an explicit stack (so nesting never
/// recurses), type-checks them (§10.3), and simplifies as it builds (§10.10): constants fold, sums merge like units
/// and sort, products distribute over sums of constants.
/// </summary>
internal sealed class CalcParser
{
    public const int MaxNodes = 1 << 16;

    private readonly List<CalcNode> _nodes = [];
    private readonly List<int> _args = [];
    private readonly Dictionary<string, double>? _symbols;
    private bool _failed;

    private CalcParser(Dictionary<string, double>? symbols) => _symbols = symbols;

    /// <summary>The outcome of a parse: a constant, or an expression that needs a context.</summary>
    internal readonly record struct Result(bool IsConstant, double Value, CssUnit Unit, CalcExpression? Expression, CalcType Type, string FunctionName);

    /// <summary>Parses the function token at <paramref name="functionIndex"/> (<c>calc</c> or a math function name).</summary>
    public static bool TryParse(TokenRange range, int functionIndex, Dictionary<string, double>? symbols, out Result result)
    {
        result = default;
        string name = range.IdentString(functionIndex);
        if (!IsMathFunctionName(name))
        {
            return false;
        }

        var p = new CalcParser(symbols);
        int root = p.Run(range, functionIndex);
        if (root < 0 || p._failed)
        {
            return false;
        }

        CalcNode n = p._nodes[root];
        if (n.Op == CalcOp.Const)
        {
            result = new Result(true, n.Value, n.Unit, null, n.Type, name);
            return true;
        }

        result = new Result(false, 0, CssUnit.None, p.Compact(root), n.Type, name);
        return true;
    }

    public static bool IsMathFunctionName(ReadOnlySpan<char> n) =>
        n.Equals("calc", StringComparison.OrdinalIgnoreCase) || FunctionOp(n, out _);

    private static bool FunctionOp(ReadOnlySpan<char> n, out CalcOp op)
    {
        op = CalcOp.Const;
        string s = n.ToString().AsciiLower();
        switch (s)
        {
            case "min": op = CalcOp.Min; return true;
            case "max": op = CalcOp.Max; return true;
            case "clamp": op = CalcOp.Clamp; return true;
            case "round": op = CalcOp.Round; return true;
            case "mod": op = CalcOp.Mod; return true;
            case "rem": op = CalcOp.Rem; return true;
            case "sin": op = CalcOp.Sin; return true;
            case "cos": op = CalcOp.Cos; return true;
            case "tan": op = CalcOp.Tan; return true;
            case "asin": op = CalcOp.Asin; return true;
            case "acos": op = CalcOp.Acos; return true;
            case "atan": op = CalcOp.Atan; return true;
            case "atan2": op = CalcOp.Atan2; return true;
            case "pow": op = CalcOp.Pow; return true;
            case "sqrt": op = CalcOp.Sqrt; return true;
            case "hypot": op = CalcOp.Hypot; return true;
            case "log": op = CalcOp.Log; return true;
            case "exp": op = CalcOp.Exp; return true;
            case "abs": op = CalcOp.Abs; return true;
            case "sign": op = CalcOp.Sign; return true;
            default: return false;
        }
    }

    private sealed class Frame
    {
        public CalcOp Op;          // Const means calc() or a parenthesized group
        public bool IsGroup;
        public int Pos;
        public int End;
        public readonly List<int> Results = [];
        public readonly List<int> Operands = [];
        public readonly List<char> Operators = [];
        public bool ExpectOperand = true;
        public bool SawWs;
        public byte Strategy;
        public bool StrategyDone;
        public bool SawComma;
    }

    private int Run(TokenRange r, int fn)
    {
        var stack = new Stack<Frame>();
        stack.Push(NewFrame(r, fn, groupParen: false));
        int rootResult = -1;
        while (stack.Count > 0 && !_failed)
        {
            Frame f = stack.Peek();
            if (f.Pos >= f.End)
            {
                int res = Finish(f);
                if (res < 0)
                {
                    return -1;
                }

                stack.Pop();
                if (stack.Count == 0)
                {
                    rootResult = res;
                    break;
                }

                Frame parent = stack.Peek();
                parent.Operands.Add(res);
                parent.ExpectOperand = false;
                continue;
            }

            int i = f.Pos;
            CssTokenKind k = r.Kind(i);
            switch (k)
            {
                case CssTokenKind.Whitespace:
                    f.SawWs = true;
                    f.Pos++;
                    continue;
                case CssTokenKind.Number:
                case CssTokenKind.Percentage:
                case CssTokenKind.Dimension:
                    if (!f.ExpectOperand || !PushConst(f, r, i))
                    {
                        return Fail();
                    }

                    f.Pos++;
                    f.SawWs = false;
                    continue;
                case CssTokenKind.Ident:
                    if (!f.ExpectOperand)
                    {
                        return Fail();
                    }

                    if (f.Op == CalcOp.Round && !f.StrategyDone && f.Results.Count == 0 && TryStrategy(r.IdentString(i), out byte st))
                    {
                        f.Strategy = st;
                        f.StrategyDone = true;
                        f.Pos++;
                        f.SawComma = false;
                        // The comma after the strategy keyword is consumed by the comma case; remember that no operand came.
                        f.ExpectOperand = true;
                        f.Operands.Clear();
                        f.Results.Clear();
                        f.StrategyDone = true;
                        // Mark the strategy slot by skipping one comma.
                        int j = r.SkipWs(f.Pos);
                        if (j < f.End && r.Kind(j) == CssTokenKind.Comma)
                        {
                            f.Pos = j + 1;
                            continue;
                        }

                        return Fail();
                    }

                    if (!PushIdent(f, r.IdentString(i)))
                    {
                        return Fail();
                    }

                    f.Pos++;
                    f.SawWs = false;
                    continue;
                case CssTokenKind.Function:
                {
                    if (!f.ExpectOperand)
                    {
                        return Fail();
                    }

                    ReadOnlySpan<char> name = r.Name(i, out _);
                    if (!name.Equals("calc", StringComparison.OrdinalIgnoreCase) && !FunctionOp(name, out _))
                    {
                        return Fail();
                    }

                    if (_nodes.Count > MaxNodes)
                    {
                        return Fail();
                    }

                    int close = r.Close(i);
                    f.Pos = Math.Min(close + 1, f.End);
                    f.SawWs = false;
                    stack.Push(NewFrame(r, i, groupParen: false));
                    continue;
                }

                case CssTokenKind.LeftParen:
                {
                    if (!f.ExpectOperand)
                    {
                        return Fail();
                    }

                    int close = r.Close(i);
                    f.Pos = Math.Min(close + 1, f.End);
                    f.SawWs = false;
                    stack.Push(NewFrame(r, i, groupParen: true));
                    continue;
                }

                case CssTokenKind.Comma:
                    if (f.IsGroup || f.Op == CalcOp.Const || f.ExpectOperand)
                    {
                        return Fail();
                    }

                    if (!EndArg(f))
                    {
                        return Fail();
                    }

                    f.SawComma = true;
                    f.Pos++;
                    f.SawWs = false;
                    continue;
                case CssTokenKind.Delim:
                {
                    char c = r.Tree.Text[r.Tree.Tokens[i].Start];
                    if (c is not ('+' or '-' or '*' or '/') || f.ExpectOperand)
                    {
                        return Fail();
                    }

                    bool additive = c is '+' or '-';
                    if (additive && (!f.SawWs || i + 1 >= f.End || r.Kind(i + 1) != CssTokenKind.Whitespace))
                    {
                        return Fail();
                    }

                    ReduceFor(f, c);
                    f.Operators.Add(c);
                    f.ExpectOperand = true;
                    f.Pos++;
                    f.SawWs = false;
                    continue;
                }

                default:
                    return Fail();
            }
        }

        return _failed ? -1 : rootResult;
    }

    private int Fail()
    {
        _failed = true;
        return -1;
    }

    private static Frame NewFrame(TokenRange r, int opener, bool groupParen)
    {
        var f = new Frame { IsGroup = groupParen, Pos = opener + 1, End = r.Close(opener) };
        if (!groupParen)
        {
            FunctionOp(r.Name(opener, out _), out CalcOp op);
            f.Op = op;
        }

        return f;
    }

    private static bool TryStrategy(string s, out byte strategy)
    {
        strategy = 0;
        switch (s.AsciiLower())
        {
            case "nearest": strategy = 0; return true;
            case "up": strategy = 1; return true;
            case "down": strategy = 2; return true;
            case "to-zero": strategy = 3; return true;
            default: return false;
        }
    }

    private bool PushConst(Frame f, TokenRange r, int i)
    {
        double v = r.Number(i);
        CssUnit unit;
        switch (r.Kind(i))
        {
            case CssTokenKind.Number:
                unit = CssUnit.None;
                break;
            case CssTokenKind.Percentage:
                unit = CssUnit.Percent;
                break;
            default:
                if (!r.TryUnit(i, out unit))
                {
                    return false;
                }

                break;
        }

        f.Operands.Add(AddConst(v, unit));
        f.ExpectOperand = false;
        return true;
    }

    private int AddConst(double v, CssUnit unit)
    {
        CssDimension d = CssUnits.GetDimension(unit);
        CssUnit canon = CssUnits.GetCanonicalUnit(d);
        bool relative = unit == CssUnit.Percent || (d == CssDimension.Length && !CssUnits.IsAbsoluteLength(unit) && unit != CssUnit.Px);
        if (d is CssDimension.Length or CssDimension.Angle or CssDimension.Time or CssDimension.Frequency or CssDimension.Resolution && !relative)
        {
            v *= CssUnits.GetCanonicalFactor(unit);
            unit = canon;
        }

        return AddNode(new CalcNode { Op = CalcOp.Const, Value = v, Unit = unit, Relative = relative, Type = CalcType.OfDimension(d) });
    }

    private bool PushIdent(Frame f, string ident)
    {
        double v;
        switch (ident.AsciiLower())
        {
            case "e": v = Math.E; break;
            case "pi": v = Math.PI; break;
            case "infinity": v = double.PositiveInfinity; break;
            case "-infinity": v = double.NegativeInfinity; break;
            case "nan": v = double.NaN; break;
            case "none" when f.Op == CalcOp.Clamp && f.Results.Count is 0 or 2:
            {
                // clamp() accepts none for either bound; it takes the type of the other operands.
                int idx = AddNode(new CalcNode
                {
                    Op = CalcOp.Const,
                    Value = f.Results.Count == 0 ? double.NegativeInfinity : double.PositiveInfinity,
                    Strategy = 255,
                    Type = CalcType.Number,
                });
                f.Operands.Add(idx);
                f.ExpectOperand = false;
                return true;
            }

            default:
                if (_symbols is null || !_symbols.TryGetValue(ident.AsciiLower(), out v))
                {
                    return false;
                }

                break;
        }

        f.Operands.Add(AddConst(v, CssUnit.None));
        f.ExpectOperand = false;
        return true;
    }

    private int AddNode(CalcNode n)
    {
        if (_nodes.Count >= MaxNodes)
        {
            _failed = true;
            return 0;
        }

        _nodes.Add(n);
        return _nodes.Count - 1;
    }

    private int AddNary(CalcOp op, List<int> children, CalcType type, bool relative)
    {
        int start = _args.Count;
        _args.AddRange(children);
        return AddNode(new CalcNode { Op = op, ArgStart = start, ArgCount = children.Count, Type = type, Relative = relative });
    }

    private static int Precedence(char c) => c is '*' or '/' ? 2 : 1;

    private void ReduceFor(Frame f, char incoming)
    {
        while (f.Operators.Count > 0 && Precedence(f.Operators[^1]) >= Precedence(incoming) && !_failed)
        {
            ApplyTop(f);
        }
    }

    private void ApplyTop(Frame f)
    {
        char op = f.Operators[^1];
        f.Operators.RemoveAt(f.Operators.Count - 1);
        if (f.Operands.Count < 2)
        {
            // An operator without both operands, as in round(7*up, ...) where a keyword stands in for a value.
            _failed = true;
            return;
        }

        int b = f.Operands[^1];
        int a = f.Operands[^2];
        f.Operands.RemoveRange(f.Operands.Count - 2, 2);
        if (_nodes[a].Strategy == 255 || _nodes[b].Strategy == 255)
        {
            // Values 4 §10.8: clamp()'s `none` stands alone as a bound; it is not a value to compute with.
            _failed = true;
            f.Operands.Add(a);
            return;
        }

        int res = op switch
        {
            '+' => Add(a, b, negate: false),
            '-' => Add(a, b, negate: true),
            '*' => Mul(a, b),
            _ => Div(a, b),
        };
        f.Operands.Add(res);
    }

    private bool EndArg(Frame f)
    {
        if (f.ExpectOperand)
        {
            return false;
        }

        while (f.Operators.Count > 0 && !_failed)
        {
            ApplyTop(f);
        }

        if (f.Operands.Count != 1)
        {
            return false;
        }

        f.Results.Add(f.Operands[0]);
        f.Operands.Clear();
        f.ExpectOperand = true;
        return !_failed;
    }

    // --- algebra -------------------------------------------------------------------------------------------------

    private IEnumerable<int> Terms(int node)
    {
        CalcNode n = _nodes[node];
        if (n.Op == CalcOp.Sum)
        {
            for (int k = 0; k < n.ArgCount; k++)
            {
                yield return _args[n.ArgStart + k];
            }
        }
        else
        {
            yield return node;
        }
    }

    private int Negated(int term)
    {
        CalcNode n = _nodes[term];
        if (n.Op == CalcOp.Const)
        {
            n.Value = -n.Value;
            return AddNode(n);
        }

        if (n.Op == CalcOp.Negate)
        {
            return _args[n.ArgStart];
        }

        if (n.Op == CalcOp.Sum)
        {
            var list = new List<int>();
            foreach (int t in Terms(term))
            {
                list.Add(Negated(t));
            }

            return BuildSum(list);
        }

        return AddNary(CalcOp.Negate, [term], n.Type, n.Relative);
    }

    private int Add(int a, int b, bool negate)
    {
        var list = new List<int>();
        list.AddRange(Terms(a));
        foreach (int t in Terms(b))
        {
            list.Add(negate ? Negated(t) : t);
        }

        return BuildSum(list);
    }

    private int BuildSum(List<int> terms)
    {
        if (_failed)
        {
            return 0;
        }

        CalcType type = _nodes[terms[0]].Type;
        // Type of a sum: all terms must add (§10.3); a clamp() "none" bound is typeless.
        for (int k = 1; k < terms.Count; k++)
        {
            if (!type.TryAdd(_nodes[terms[k]].Type, out type))
            {
                _failed = true;
                return 0;
            }
        }

        // Merge constants with the same unit (§10.10: "combine numbers, combine percentages, combine px values").
        var merged = new List<int>();
        var byUnit = new Dictionary<CssUnit, int>();
        foreach (int t in terms)
        {
            CalcNode n = _nodes[t];
            if (n.Op == CalcOp.Const)
            {
                if (byUnit.TryGetValue(n.Unit, out int at))
                {
                    CalcNode m = _nodes[at];
                    m.Value += n.Value;
                    _nodes[at] = m;
                }
                else
                {
                    CalcNode copy = n;
                    int idx = AddNode(copy);
                    byUnit[n.Unit] = idx;
                    merged.Add(idx);
                }
            }
            else
            {
                merged.Add(t);
            }
        }

        // Sort (§10.13): the number, the percentage, dimensions by unit name, then everything else in order.
        var ordered = new List<int>(merged.Count);
        foreach (int t in merged)
        {
            if (_nodes[t] is { Op: CalcOp.Const, Unit: CssUnit.None })
            {
                ordered.Add(t);
            }
        }

        foreach (int t in merged)
        {
            if (_nodes[t] is { Op: CalcOp.Const, Unit: CssUnit.Percent })
            {
                ordered.Add(t);
            }
        }

        var dims = new List<int>();
        foreach (int t in merged)
        {
            if (_nodes[t] is { Op: CalcOp.Const } c && c.Unit is not (CssUnit.None or CssUnit.Percent))
            {
                dims.Add(t);
            }
        }

        dims.Sort((x, y) => string.Compare(CssUnits.GetName(_nodes[x].Unit), CssUnits.GetName(_nodes[y].Unit), StringComparison.OrdinalIgnoreCase));
        ordered.AddRange(dims);
        foreach (int t in merged)
        {
            if (_nodes[t].Op != CalcOp.Const)
            {
                ordered.Add(t);
            }
        }

        if (ordered.Count == 1)
        {
            return ordered[0];
        }

        bool rel = false;
        foreach (int t in ordered)
        {
            rel |= _nodes[t].Relative;
        }

        return AddNary(CalcOp.Sum, ordered, type, rel);
    }

    private int Mul(int a, int b)
    {
        CalcNode na = _nodes[a];
        CalcNode nb = _nodes[b];
        CalcType type = na.Type.Mul(nb.Type);
        if (na.Op == CalcOp.Const && nb.Op == CalcOp.Const)
        {
            if (na.Unit == CssUnit.None)
            {
                return AddConstNode(na.Value * nb.Value, nb.Unit, nb.Relative, type);
            }

            if (nb.Unit == CssUnit.None)
            {
                return AddConstNode(na.Value * nb.Value, na.Unit, na.Relative, type);
            }
        }

        // number × Sum of constants distributes (§10.10).
        if (na.Op == CalcOp.Const && na.Unit == CssUnit.None && nb.Op == CalcOp.Sum)
        {
            return Distribute(b, na.Value);
        }

        if (nb.Op == CalcOp.Const && nb.Unit == CssUnit.None && na.Op == CalcOp.Sum)
        {
            return Distribute(a, nb.Value);
        }

        var factors = new List<int>();
        double number = 1;
        bool haveNumber = false;
        foreach (int side in new[] { a, b })
        {
            CalcNode n = _nodes[side];
            IEnumerable<int> parts = n.Op == CalcOp.Product ? ProductFactors(side) : [side];
            foreach (int p in parts)
            {
                CalcNode pn = _nodes[p];
                if (pn.Op == CalcOp.Const && pn.Unit == CssUnit.None)
                {
                    number *= pn.Value;
                    haveNumber = true;
                }
                else
                {
                    factors.Add(p);
                }
            }
        }

        if (haveNumber && number != 1)
        {
            factors.Insert(0, AddConst(number, CssUnit.None));
        }

        if (factors.Count == 1)
        {
            return factors[0];
        }

        bool rel = false;
        foreach (int f in factors)
        {
            rel |= _nodes[f].Relative;
        }

        return AddNary(CalcOp.Product, factors, type, rel);
    }

    private IEnumerable<int> ProductFactors(int node)
    {
        CalcNode n = _nodes[node];
        for (int k = 0; k < n.ArgCount; k++)
        {
            yield return _args[n.ArgStart + k];
        }
    }

    private int AddConstNode(double v, CssUnit unit, bool relative, CalcType type) =>
        AddNode(new CalcNode { Op = CalcOp.Const, Value = v, Unit = unit, Relative = relative, Type = type });

    private int Distribute(int sum, double factor)
    {
        var scaled = new List<int>();
        foreach (int t in Terms(sum))
        {
            CalcNode n = _nodes[t];
            if (n.Op == CalcOp.Const)
            {
                n.Value *= factor;
                scaled.Add(AddNode(n));
            }
            else
            {
                scaled.Add(Mul(AddConst(factor, CssUnit.None), t));
            }
        }

        return BuildSum(scaled);
    }

    private int Div(int a, int b)
    {
        CalcNode na = _nodes[a];
        CalcNode nb = _nodes[b];
        if (nb.Op == CalcOp.Const && nb.Unit == CssUnit.None)
        {
            if (na.Op == CalcOp.Const)
            {
                return AddConstNode(na.Value / nb.Value, na.Unit, na.Relative, na.Type);
            }

            return Mul(a, AddConst(1 / nb.Value, CssUnit.None));
        }

        if (na.Op == CalcOp.Const && nb.Op == CalcOp.Const && na.Unit == nb.Unit)
        {
            return AddConstNode(na.Value / nb.Value, CssUnit.None, relative: false, CalcType.Number);
        }

        int inv = AddNary(CalcOp.Invert, [b], nb.Type.Invert(), nb.Relative);
        return Mul(a, inv);
    }

    // --- finishing a frame --------------------------------------------------------------------------------------

    private int Finish(Frame f)
    {
        if (!EndArg(f))
        {
            return Fail();
        }

        List<int> a = f.Results;
        if (f.IsGroup || f.Op == CalcOp.Const)
        {
            return a.Count == 1 ? a[0] : Fail();
        }

        return Function(f, a);
    }

    private int Function(Frame f, List<int> a)
    {
        CalcOp op = f.Op;
        int n = a.Count;
        int minArgs = op switch
        {
            CalcOp.Clamp => 3,
            CalcOp.Round => 1,
            CalcOp.Mod or CalcOp.Rem or CalcOp.Atan2 or CalcOp.Pow => 2,
            _ => 1,
        };
        int maxArgs = op switch
        {
            CalcOp.Clamp => 3,
            CalcOp.Round => 2,
            CalcOp.Mod or CalcOp.Rem or CalcOp.Atan2 or CalcOp.Pow => 2,
            CalcOp.Min or CalcOp.Max or CalcOp.Hypot => int.MaxValue,
            CalcOp.Log => 2,
            _ => 1,
        };
        if (n < minArgs || n > maxArgs)
        {
            return Fail();
        }

        CalcType result;
        byte strategy = f.Strategy;
        switch (op)
        {
            case CalcOp.Min:
            case CalcOp.Max:
            case CalcOp.Hypot:
            case CalcOp.Clamp:
            case CalcOp.Mod:
            case CalcOp.Rem:
            case CalcOp.Atan2:
            case CalcOp.Round:
            {
                CalcType? t = null;
                foreach (int x in a)
                {
                    if (_nodes[x].Strategy == 255)
                    {
                        continue; // clamp() "none"
                    }

                    if (t is null)
                    {
                        t = _nodes[x].Type;
                    }
                    else if (!t.Value.TryAdd(_nodes[x].Type, out CalcType sum))
                    {
                        return Fail();
                    }
                    else
                    {
                        t = sum;
                    }
                }

                if (t is null)
                {
                    return Fail();
                }

                if (op == CalcOp.Round && n == 1 && !t.Value.IsNumber)
                {
                    return Fail();
                }

                if (op == CalcOp.Round && n == 1)
                {
                    a.Add(AddConst(1, CssUnit.None));
                }

                if (op == CalcOp.Atan2)
                {
                    result = CalcType.Of(CalcBase.Angle);
                }
                else
                {
                    result = t.Value;
                }

                break;
            }

            case CalcOp.Sin:
            case CalcOp.Cos:
            case CalcOp.Tan:
            {
                CalcType t = _nodes[a[0]].Type;
                if (t.IsNumber)
                {
                    strategy = 1; // radians
                }
                else if (t.Dimension != CssDimension.Angle || t.HasPercentHint)
                {
                    return Fail();
                }

                result = CalcType.Number;
                break;
            }

            case CalcOp.Asin:
            case CalcOp.Acos:
            case CalcOp.Atan:
                if (!_nodes[a[0]].Type.IsNumber)
                {
                    return Fail();
                }

                result = CalcType.Of(CalcBase.Angle);
                break;
            case CalcOp.Pow:
            case CalcOp.Sqrt:
            case CalcOp.Log:
            case CalcOp.Exp:
                foreach (int x in a)
                {
                    if (!_nodes[x].Type.IsNumber)
                    {
                        return Fail();
                    }
                }

                result = CalcType.Number;
                break;
            case CalcOp.Abs:
                result = _nodes[a[0]].Type;
                break;
            default: // Sign
                result = CalcType.Number;
                break;
        }

        bool rel = false;
        bool allConst = true;
        foreach (int x in a)
        {
            rel |= _nodes[x].Relative;
            allConst &= _nodes[x].Op == CalcOp.Const;
        }

        // Fold when every argument is a constant that can be computed without context.
        CssDimension? dim = result.Dimension;
        bool sameUnit = true;
        CssUnit firstUnit = CssUnit.None;
        bool firstSeen = false;
        foreach (int x in a)
        {
            if (_nodes[x].Strategy == 255)
            {
                continue;
            }

            if (!firstSeen)
            {
                firstUnit = _nodes[x].Unit;
                firstSeen = true;
            }
            else if (_nodes[x].Unit != firstUnit)
            {
                sameUnit = false;
            }
        }

        if (allConst && (!rel || (sameUnit && (firstUnit != CssUnit.Percent || op is CalcOp.Min or CalcOp.Max or CalcOp.Clamp) && op is not (CalcOp.Sin or CalcOp.Cos or CalcOp.Tan or CalcOp.Asin or CalcOp.Acos or CalcOp.Atan or CalcOp.Atan2 or CalcOp.Hypot or CalcOp.Pow or CalcOp.Sqrt or CalcOp.Log or CalcOp.Exp or CalcOp.Sign))))
        {
            double[] vals = new double[a.Count];
            int[] idx = new int[a.Count];
            for (int k = 0; k < a.Count; k++)
            {
                vals[k] = _nodes[a[k]].Value;
                idx[k] = k;
            }

            double v = CalcMath.Apply(op, strategy, vals, idx, 0, a.Count);
            CssUnit unit;
            bool resultRelative = false;
            if (rel)
            {
                unit = firstUnit;
                resultRelative = true;
            }
            else
            {
                unit = dim is { } d ? CssUnits.GetCanonicalUnit(d) : CssUnit.None;
            }

            if (dim is not null || result.IsNumber)
            {
                if (op is CalcOp.Sign or CalcOp.Pow)
                {
                    unit = CssUnit.None;
                }

                return AddConstNode(v, unit, resultRelative, result);
            }
        }

        if (op is CalcOp.Min or CalcOp.Max)
        {
            // Combine same-unit constants (§10.10 partial simplification of Min/Max).
            var kept = new List<int>();
            var seen = new Dictionary<CssUnit, int>();
            foreach (int x in a)
            {
                CalcNode nx = _nodes[x];
                if (nx.Op == CalcOp.Const && seen.TryGetValue(nx.Unit, out int at))
                {
                    CalcNode m = _nodes[at];
                    m.Value = op == CalcOp.Min ? Math.Min(m.Value, nx.Value) : Math.Max(m.Value, nx.Value);
                    _nodes[at] = m;
                }
                else
                {
                    kept.Add(x);
                    if (nx.Op == CalcOp.Const)
                    {
                        seen[nx.Unit] = x;
                    }
                }
            }

            if (kept.Count == 1)
            {
                return kept[0];
            }

            a = kept;
        }

        int id = AddNary(op, a, result, rel);
        CalcNode node = _nodes[id];
        node.Strategy = strategy;
        _nodes[id] = node;
        return id;
    }

    // --- compaction ---------------------------------------------------------------------------------------------

    private CalcExpression Compact(int root)
    {
        var keep = new bool[_nodes.Count];
        keep[root] = true;
        for (int i = root; i >= 0; i--)
        {
            if (!keep[i])
            {
                continue;
            }

            CalcNode n = _nodes[i];
            for (int k = 0; k < n.ArgCount; k++)
            {
                keep[_args[n.ArgStart + k]] = true;
            }
        }

        int[] map = new int[_nodes.Count];
        var nodes = new List<CalcNode>();
        var args = new List<int>();
        for (int i = 0; i <= root; i++)
        {
            if (!keep[i])
            {
                continue;
            }

            CalcNode n = _nodes[i];
            int start = args.Count;
            for (int k = 0; k < n.ArgCount; k++)
            {
                args.Add(map[_args[n.ArgStart + k]]);
            }

            n.ArgStart = start;
            map[i] = nodes.Count;
            nodes.Add(n);
        }

        return new CalcExpression(nodes.ToArray(), args.ToArray(), map[root]);
    }
}

/// <summary>Serializes a calculation tree (CSS Values 4 §10.13).</summary>
internal static class CalcSerializer
{
    public static string Serialize(CalcExpression e) => Serialize(e.Nodes, e.Args, e.Root, null);

    public static string Serialize(CalcNode[] nodes, int[] args, int root, string? functionName)
    {
        var text = new string[root + 1];
        for (int i = 0; i <= root; i++)
        {
            CalcNode n = nodes[i];
            switch (n.Op)
            {
                case CalcOp.Const:
                    text[i] = ConstText(n);
                    break;
                case CalcOp.Negate:
                    text[i] = "(-1 * " + text[args[n.ArgStart]] + ")";
                    break;
                case CalcOp.Invert:
                    text[i] = "(1 / " + text[args[n.ArgStart]] + ")";
                    break;
                case CalcOp.Sum:
                {
                    var sb = new StringBuilder("(");
                    for (int k = 0; k < n.ArgCount; k++)
                    {
                        int c = args[n.ArgStart + k];
                        CalcNode cn = nodes[c];
                        if (k == 0)
                        {
                            sb.Append(text[c]);
                        }
                        else if (cn.Op == CalcOp.Negate)
                        {
                            sb.Append(" - ").Append(text[args[cn.ArgStart]]);
                        }
                        else if (cn.Op == CalcOp.Const && (cn.Value < 0 || (cn.Value == 0 && double.IsNegative(cn.Value))))
                        {
                            sb.Append(" - ").Append(ConstText(cn with { Value = -cn.Value }));
                        }
                        else
                        {
                            sb.Append(" + ").Append(text[c]);
                        }
                    }

                    text[i] = sb.Append(')').ToString();
                    break;
                }

                case CalcOp.Product:
                {
                    var sb = new StringBuilder("(");
                    for (int k = 0; k < n.ArgCount; k++)
                    {
                        int c = args[n.ArgStart + k];
                        CalcNode cn = nodes[c];
                        if (k == 0)
                        {
                            sb.Append(text[c]);
                        }
                        else if (cn.Op == CalcOp.Invert)
                        {
                            sb.Append(" / ").Append(text[args[cn.ArgStart]]);
                        }
                        else
                        {
                            sb.Append(" * ").Append(text[c]);
                        }
                    }

                    text[i] = sb.Append(')').ToString();
                    break;
                }

                default:
                    text[i] = Function(n, nodes, args, text);
                    break;
            }
        }

        CalcNode r = nodes[root];
        string body = text[root];
        if (r.Op is CalcOp.Const or CalcOp.Sum or CalcOp.Product or CalcOp.Negate or CalcOp.Invert)
        {
            if (r.Op == CalcOp.Const && (double.IsNaN(r.Value) || double.IsInfinity(r.Value)))
            {
                return "calc(" + InfinityText(r) + ")";
            }

            return "calc(" + Strip(body) + ")";
        }

        return body;
    }

    internal static string InfinityText(CalcNode n)
    {
        string k = double.IsNaN(n.Value) ? "NaN" : n.Value > 0 ? "infinity" : "-infinity";
        CssDimension? d = n.Type.Dimension;
        if (d is null || d == CssDimension.Number)
        {
            return k;
        }

        CssUnit u = CssUnits.GetCanonicalUnit(d.Value);
        return k + " * 1" + CssUnits.GetName(u);
    }

    private static string Strip(string s) => s.Length >= 2 && s[0] == '(' && s[^1] == ')' ? s.Substring(1, s.Length - 2) : s;

    private static string ConstText(CalcNode n)
    {
        if (double.IsNaN(n.Value) || double.IsInfinity(n.Value))
        {
            return "calc(" + InfinityText(n) + ")";
        }

        return CssUnits.FormatNumber(n.Value) + CssUnits.GetName(n.Unit);
    }

    private static string Function(CalcNode n, CalcNode[] nodes, int[] args, string[] text)
    {
        var sb = new StringBuilder(FunctionName(n.Op)).Append('(');
        int start = 0;
        if (n.Op == CalcOp.Round && n.Strategy != 0)
        {
            sb.Append(n.Strategy switch { 1 => "up", 2 => "down", _ => "to-zero" }).Append(", ");
        }

        for (int k = start; k < n.ArgCount; k++)
        {
            if (k > 0)
            {
                sb.Append(", ");
            }

            int c = args[n.ArgStart + k];
            if (nodes[c].Strategy == 255)
            {
                sb.Append("none");
                continue;
            }

            sb.Append(Strip(text[c]));
        }

        return sb.Append(')').ToString();
    }

    private static string FunctionName(CalcOp op) => op.ToString().AsciiLower();
}
