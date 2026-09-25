using System.Globalization;
using System.Text;

namespace AAEmu.Game.Models.Game.Items.Loots;

/// <summary>
/// Parser/evaluator for the small SQL-WHERE subset used by the shipped drop-rule
/// matcher rows. Unsupported fields, operators, or syntax produce an invalid matcher
/// for diagnostics; this type does not select or generate runtime loot.
/// </summary>
public sealed class DropRuleMatcher
{
    private readonly Node? _root;

    private DropRuleMatcher(Node? root)
    {
        _root = root;
    }

    public bool IsValid => _root is not null;

    public static DropRuleMatcher Invalid { get; } = new(null);

    public static bool TryParse(string expression, out DropRuleMatcher? matcher, out string? error)
    {
        matcher = null;
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "matcher expression is empty";
            return false;
        }

        try
        {
            var tokens = Tokenize(expression);
            var parser = new Parser(tokens);
            var root = parser.Parse();
            matcher = new DropRuleMatcher(root);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool Matches(DropRuleSubject subject)
    {
        return _root?.Evaluate(subject) == Truth.True;
    }

    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < expression.Length)
        {
            var ch = expression[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            if (ch == '\'')
            {
                var value = new StringBuilder();
                index++;
                var closed = false;
                while (index < expression.Length)
                {
                    var current = expression[index++];
                    if (current != '\'')
                    {
                        value.Append(current);
                        continue;
                    }

                    if (index < expression.Length && expression[index] == '\'')
                    {
                        value.Append('\'');
                        index++;
                        continue;
                    }

                    closed = true;
                    break;
                }

                if (!closed)
                    throw new FormatException("unterminated string literal");
                tokens.Add(new Token(TokenKind.String, value.ToString()));
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = index++;
                while (index < expression.Length &&
                       (char.IsLetterOrDigit(expression[index]) || expression[index] == '_'))
                {
                    index++;
                }
                tokens.Add(new Token(TokenKind.Identifier, expression[start..index]));
                continue;
            }

            if (char.IsDigit(ch) ||
                (ch == '-' && index + 1 < expression.Length && char.IsDigit(expression[index + 1])))
            {
                var start = index++;
                while (index < expression.Length && char.IsDigit(expression[index]))
                    index++;
                if (index < expression.Length && expression[index] == '.')
                {
                    index++;
                    while (index < expression.Length && char.IsDigit(expression[index]))
                        index++;
                }
                tokens.Add(new Token(TokenKind.Number, expression[start..index]));
                continue;
            }

            var symbol = ch switch
            {
                '(' => ConsumeOperator(expression, ref index, "("),
                ')' => ConsumeOperator(expression, ref index, ")"),
                ',' => ConsumeOperator(expression, ref index, ","),
                '<' => ConsumeOperator(expression, ref index, "<"),
                '>' => ConsumeOperator(expression, ref index, ">"),
                '=' => ConsumeOperator(expression, ref index, "="),
                '!' => index + 1 < expression.Length && expression[index + 1] == '='
                    ? ConsumeOperator(expression, ref index, "!=")
                    : throw new FormatException($"unsupported operator at {index}"),
                _ => throw new FormatException($"unsupported token '{ch}' at {index}")
            };
            tokens.Add(new Token(TokenKind.Symbol, symbol));
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private static string ConsumeOperator(string expression, ref int index, string fallback)
    {
        var first = expression[index];
        if (index + 1 < expression.Length &&
            (expression[index + 1] == '=' || expression[index + 1] == '>'))
        {
            var second = expression[index + 1];
            index += 2;
            return first.ToString() + second.ToString();
        }
        index++;
        return fallback;
    }

    private enum TokenKind
    {
        End,
        Identifier,
        Number,
        String,
        Symbol
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public Node Parse()
        {
            var result = ParseOr();
            if (Current.Kind != TokenKind.End)
                throw new FormatException($"unexpected token '{Current.Text}'");
            return result;
        }

        private Token Current => tokens[_position];

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (MatchKeyword("OR"))
                left = new BinaryNode(left, ParseAnd(), BinaryOperator.Or);
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (MatchKeyword("AND"))
                left = new BinaryNode(left, ParseUnary(), BinaryOperator.And);
            return left;
        }

        private Node ParseUnary()
        {
            if (MatchKeyword("NOT"))
                return new NotNode(ParseUnary());

            if (MatchSymbol("("))
            {
                var nested = ParseOr();
                ExpectSymbol(")");
                return nested;
            }

            return ParsePredicate();
        }

        private Node ParsePredicate()
        {
            var field = ExpectIdentifier();
            if (!DropRuleFields.IsKnown(field))
                throw new FormatException($"unsupported matcher field '{field}'");

            if (MatchKeyword("NOT"))
            {
                if (MatchKeyword("LIKE"))
                    return new LikeNode(field, ExpectString(), true);
                if (MatchKeyword("IN"))
                    return new InNode(field, ParseLiterals(), true);
                throw new FormatException("NOT must be followed by LIKE or IN");
            }

            if (MatchKeyword("LIKE"))
                return new LikeNode(field, ExpectString(), false);
            if (MatchKeyword("IN"))
                return new InNode(field, ParseLiterals(), false);

            var op = ExpectComparisonOperator();
            var literal = ParseLiteral();
            return new ComparisonNode(field, op, literal);
        }

        private List<Literal> ParseLiterals()
        {
            ExpectSymbol("(");
            var values = new List<Literal>();
            if (!MatchSymbol(")"))
            {
                do
                {
                    values.Add(ParseLiteral());
                }
                while (MatchSymbol(","));
                ExpectSymbol(")");
            }
            return values;
        }

        private Literal ParseLiteral()
        {
            if (Current.Kind == TokenKind.Number &&
                double.TryParse(Current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                _position++;
                return Literal.FromNumber(number);
            }

            if (Current.Kind == TokenKind.String)
            {
                var value = Current.Text;
                _position++;
                return Literal.FromString(value);
            }

            if (Current.Kind == TokenKind.Identifier)
            {
                var value = Current.Text;
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return Literal.FromBoolean(true);
                }
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return Literal.FromBoolean(false);
                }
            }

            throw new FormatException("expected a literal value");
        }

        private string ExpectIdentifier()
        {
            if (Current.Kind != TokenKind.Identifier)
                throw new FormatException("expected a field name");
            return Advance().Text.ToLowerInvariant();
        }

        private string ExpectString()
        {
            if (Current.Kind != TokenKind.String)
                throw new FormatException("expected a string literal");
            return Advance().Text;
        }

        private ComparisonOperator ExpectComparisonOperator()
        {
            if (Current.Kind != TokenKind.Symbol)
                throw new FormatException("expected a comparison operator");
            return Advance().Text switch
            {
                "=" => ComparisonOperator.Equal,
                "==" => ComparisonOperator.Equal,
                "!=" or "<>" => ComparisonOperator.NotEqual,
                "<" => ComparisonOperator.Less,
                "<=" => ComparisonOperator.LessOrEqual,
                ">" => ComparisonOperator.Greater,
                ">=" => ComparisonOperator.GreaterOrEqual,
                var op => throw new FormatException($"unsupported comparison operator '{op}'")
            };
        }

        private bool MatchKeyword(string keyword)
        {
            if (Current.Kind != TokenKind.Identifier ||
                !Current.Text.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
            _position++;
            return true;
        }

        private bool MatchSymbol(string symbol)
        {
            if (Current.Kind != TokenKind.Symbol || Current.Text != symbol)
                return false;
            _position++;
            return true;
        }

        private void ExpectSymbol(string symbol)
        {
            if (!MatchSymbol(symbol))
                throw new FormatException($"expected '{symbol}'");
        }

        private Token Advance() => tokens[_position++];
    }

    private abstract class Node
    {
        public abstract Truth Evaluate(DropRuleSubject subject);
    }

    private sealed class BinaryNode(Node left, Node right, BinaryOperator op) : Node
    {
        public override Truth Evaluate(DropRuleSubject subject)
        {
            var leftValue = left.Evaluate(subject);
            var rightValue = right.Evaluate(subject);
            return op switch
            {
                BinaryOperator.And when leftValue == Truth.False || rightValue == Truth.False => Truth.False,
                BinaryOperator.And when leftValue == Truth.Unknown || rightValue == Truth.Unknown => Truth.Unknown,
                BinaryOperator.And => Truth.True,
                BinaryOperator.Or when leftValue == Truth.True || rightValue == Truth.True => Truth.True,
                BinaryOperator.Or when leftValue == Truth.Unknown || rightValue == Truth.Unknown => Truth.Unknown,
                BinaryOperator.Or => Truth.False,
                _ => Truth.False
            };
        }
    }

    private sealed class NotNode(Node child) : Node
    {
        public override Truth Evaluate(DropRuleSubject subject) => child.Evaluate(subject) switch
        {
            Truth.True => Truth.False,
            Truth.False => Truth.True,
            _ => Truth.Unknown
        };
    }

    private sealed class ComparisonNode(string field, ComparisonOperator op, Literal literal) : Node
    {
        public override Truth Evaluate(DropRuleSubject subject)
        {
            var value = DropRuleFields.Read(subject, field);
            if (value.IsNull || !value.IsCompatible(literal.Kind))
                return Truth.Unknown;
            if (op is ComparisonOperator.Less or ComparisonOperator.LessOrEqual or
                ComparisonOperator.Greater or ComparisonOperator.GreaterOrEqual)
            {
                if (value.Kind != LiteralKind.Number || literal.Kind != LiteralKind.Number)
                    return Truth.Unknown;
            }

            return op switch
            {
                ComparisonOperator.Equal => value.Equals(literal) ? Truth.True : Truth.False,
                ComparisonOperator.NotEqual => value.Equals(literal) ? Truth.False : Truth.True,
                ComparisonOperator.Less => value.CompareTo(literal) < 0 ? Truth.True : Truth.False,
                ComparisonOperator.LessOrEqual => value.CompareTo(literal) <= 0 ? Truth.True : Truth.False,
                ComparisonOperator.Greater => value.CompareTo(literal) > 0 ? Truth.True : Truth.False,
                ComparisonOperator.GreaterOrEqual => value.CompareTo(literal) >= 0 ? Truth.True : Truth.False,
                _ => Truth.False
            };
        }
    }

    private sealed class LikeNode(string field, string pattern, bool negate) : Node
    {
        public override Truth Evaluate(DropRuleSubject subject)
        {
            var value = DropRuleFields.Read(subject, field);
            if (value.IsNull || value.Kind != LiteralKind.String)
                return Truth.Unknown;
            var result = SqlLike(value.StringValue, pattern);
            return negate
                ? result ? Truth.False : Truth.True
                : result ? Truth.True : Truth.False;
        }
    }

    private sealed class InNode(string field, IReadOnlyList<Literal> values, bool negate) : Node
    {
        public override Truth Evaluate(DropRuleSubject subject)
        {
            var value = DropRuleFields.Read(subject, field);
            if (value.IsNull)
                return Truth.Unknown;
            var result = values.Any(literal => value.IsCompatible(literal.Kind) && value.Equals(literal));
            return negate
                ? result ? Truth.False : Truth.True
                : result ? Truth.True : Truth.False;
        }
    }

    private enum Truth
    {
        False,
        True,
        Unknown
    }

    private enum BinaryOperator
    {
        And,
        Or
    }

    private enum ComparisonOperator
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    private enum LiteralKind
    {
        Number,
        String,
        Boolean
    }

    private readonly record struct Literal(LiteralKind Kind, double Number, string StringValue, bool BooleanValue)
    {
        public static Literal FromNumber(double value) => new(LiteralKind.Number, value, string.Empty, false);
        public static Literal FromString(string value) => new(LiteralKind.String, 0, value, false);
        public static Literal FromBoolean(bool value) => new(LiteralKind.Boolean, 0, string.Empty, value);
    }

    private readonly record struct FieldValue(LiteralKind Kind, double Number, string StringValue, bool BooleanValue, bool IsNull = false)
    {
        public bool IsCompatible(LiteralKind kind) =>
            !IsNull && (Kind == kind || (Kind == LiteralKind.Boolean && kind == LiteralKind.String));
        public bool Equals(Literal literal)
        {
            if (IsNull)
                return false;

            if (Kind == LiteralKind.Boolean && literal.Kind == LiteralKind.String)
            {
                return BooleanValue == literal.StringValue.ToLowerInvariant() switch
                {
                    "t" or "true" or "1" => true,
                    "f" or "false" or "0" => false,
                    _ => false
                };
            }

            return Kind == literal.Kind && Kind switch
            {
                LiteralKind.Number => Number.Equals(literal.Number),
                LiteralKind.String => string.Equals(StringValue, literal.StringValue, StringComparison.Ordinal),
                LiteralKind.Boolean => BooleanValue == literal.BooleanValue,
                _ => false
            };
        }
        public int CompareTo(Literal literal) => !IsNull && Kind == LiteralKind.Number && literal.Kind == LiteralKind.Number
            ? Number.CompareTo(literal.Number)
            : throw new InvalidOperationException("ordered comparison requires numeric values");
    }

    private static class DropRuleFields
    {
        public static bool IsKnown(string field) => field.ToLowerInvariant() switch
        {
            "level" or "npc_tendency_id" or "npc_grade_id" or "npc_kind_id" or
                "npc_nickname_id" or "heir_level" => true,
            "name" or "comment1" or "comment2" or "comment3" => true,
            "aggression" => true,
            _ => false
        };

        public static FieldValue Read(DropRuleSubject subject, string field) => field.ToLowerInvariant() switch
        {
            "level" => Number(subject.Level),
            "npc_tendency_id" => Number(subject.NpcTendencyId),
            "npc_grade_id" => Number(subject.NpcGradeId),
            "npc_kind_id" => Number(subject.NpcKindId),
            "npc_nickname_id" => Number(subject.NpcNicknameId),
            "heir_level" => Number(subject.HeirLevel),
            "name" => Text(subject.Name),
            "comment1" => Text(subject.Comment1),
            "comment2" => Text(subject.Comment2),
            "comment3" => Text(subject.Comment3),
            "aggression" => Bool(subject.Aggression),
            _ => throw new InvalidOperationException($"unsupported matcher field '{field}'")
        };

        private static FieldValue Number(int? value) =>
            value.HasValue
                ? new FieldValue(LiteralKind.Number, value.Value, string.Empty, false)
                : new FieldValue(LiteralKind.Number, 0, string.Empty, false, true);
        private static FieldValue Text(string? value) =>
            value is null
                ? new FieldValue(LiteralKind.String, 0, string.Empty, false, true)
                : new FieldValue(LiteralKind.String, 0, value, false);
        private static FieldValue Bool(bool? value) =>
            value.HasValue
                ? new FieldValue(LiteralKind.Boolean, 0, string.Empty, value.Value)
                : new FieldValue(LiteralKind.Boolean, 0, string.Empty, false, true);
    }

    private static bool SqlLike(string value, string pattern)
    {
        value ??= string.Empty;
        pattern ??= string.Empty;
        var valueIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var retryValueIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '_' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                valueIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '%')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
                continue;
            }

            if (starIndex < 0)
                return false;

            patternIndex = starIndex + 1;
            valueIndex = ++retryValueIndex;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '%')
            patternIndex++;
        return patternIndex == pattern.Length;
    }
}
