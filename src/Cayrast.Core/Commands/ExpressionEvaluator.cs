using System.Globalization;

namespace Cayrast.Core.Commands;

/// <summary>Raised when an expression cannot be parsed or evaluated.</summary>
/// <remarks>
/// Carries a message meant to be shown to the user, so it explains what is wrong with
/// the expression rather than describing parser internals.
/// </remarks>
public sealed class ExpressionException(string message) : Exception(message);

/// <summary>
/// A small recursive-descent evaluator for arithmetic expressions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why hand-written rather than a scripting engine.</b> The obvious shortcut for a
/// calculator is to hand the string to a JavaScript or C# evaluator. That would turn
/// the search box into an arbitrary code execution surface: a user pasting an
/// expression from a web page would be running it. This parser understands numbers and
/// operators and nothing else, so there is no code path from typed text to executed
/// code.
/// </para>
/// <para>
/// It also runs on every keystroke behind <c>calc</c>'s live preview, so it must be
/// allocation-light and fail fast on the half-typed input it will mostly see.
/// </para>
/// </remarks>
public static class ExpressionEvaluator
{
    /// <summary>Evaluates an arithmetic expression.</summary>
    /// <param name="expression">The text to evaluate, e.g. <c>20*50</c>.</param>
    /// <returns>The computed value.</returns>
    /// <exception cref="ExpressionException">The expression is malformed.</exception>
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ExpressionException("Enter an expression.");
        }

        var parser = new Parser(expression);
        var value = parser.ParseExpression();
        parser.ExpectEnd();

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ExpressionException("The result is not a finite number.");
        }

        return value;
    }

    /// <summary>Formats a result for display, avoiding misleading precision.</summary>
    /// <remarks>
    /// Binary floating point makes <c>0.1 + 0.2</c> print as <c>0.30000000000000004</c>,
    /// which is technically accurate and completely useless in a calculator. Rounding
    /// to twelve significant digits hides the representation artefact while keeping far
    /// more precision than anyone types.
    /// </remarks>
    public static string Format(double value)
    {
        var rounded = Math.Round(value, 12);

        // Integral results should not gain a decimal point.
        if (Math.Abs(rounded - Math.Truncate(rounded)) < double.Epsilon && Math.Abs(rounded) < 1e15)
        {
            return ((long)rounded).ToString("N0", CultureInfo.InvariantCulture);
        }

        return rounded.ToString("G12", CultureInfo.InvariantCulture);
    }

    /// <summary>Recursive-descent parser over the expression text.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        /// <summary>expression := term (('+' | '-') term)*</summary>
        public double ParseExpression()
        {
            var value = ParseTerm();

            while (true)
            {
                SkipWhitespace();

                if (Match('+'))
                {
                    value += ParseTerm();
                }
                else if (Match('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        /// <summary>term := power (('*' | '/' | '%') power)*</summary>
        private double ParseTerm()
        {
            var value = ParsePower();

            while (true)
            {
                SkipWhitespace();

                if (Match('*'))
                {
                    value *= ParsePower();
                }
                else if (Match('/'))
                {
                    var divisor = ParsePower();

                    // Reported rather than allowed to produce Infinity, which would
                    // surface as an unhelpful "not a finite number" further up.
                    if (divisor == 0)
                    {
                        throw new ExpressionException("Cannot divide by zero.");
                    }

                    value /= divisor;
                }
                else if (Match('%'))
                {
                    var divisor = ParsePower();
                    if (divisor == 0)
                    {
                        throw new ExpressionException("Cannot take a remainder by zero.");
                    }

                    value %= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        /// <summary>power := unary ('^' power)? — right-associative, so 2^3^2 is 512.</summary>
        private double ParsePower()
        {
            var value = ParseUnary();
            SkipWhitespace();

            return Match('^') ? Math.Pow(value, ParsePower()) : value;
        }

        /// <summary>unary := ('-' | '+')* primary</summary>
        private double ParseUnary()
        {
            SkipWhitespace();

            if (Match('-'))
            {
                return -ParseUnary();
            }

            if (Match('+'))
            {
                return ParseUnary();
            }

            return ParsePrimary();
        }

        /// <summary>primary := number | '(' expression ')' | identifier</summary>
        private double ParsePrimary()
        {
            SkipWhitespace();

            if (_position >= text.Length)
            {
                throw new ExpressionException("The expression is incomplete.");
            }

            if (Match('('))
            {
                var value = ParseExpression();
                SkipWhitespace();

                if (!Match(')'))
                {
                    throw new ExpressionException("Missing a closing bracket.");
                }

                return value;
            }

            var current = text[_position];

            if (char.IsAsciiDigit(current) || current == '.')
            {
                return ParseNumber();
            }

            if (char.IsAsciiLetter(current))
            {
                return ParseIdentifier();
            }

            throw new ExpressionException($"Unexpected character '{current}'.");
        }

        private double ParseNumber()
        {
            var start = _position;

            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.' || text[_position] == '_'))
            {
                _position++;
            }

            // Digit separators are accepted so pasted values such as 1_000_000 work.
            var literal = text[start.._position].Replace("_", string.Empty, StringComparison.Ordinal);

            // Invariant culture throughout: '.' is always the decimal point, because
            // expressions are also pasted from code and documentation, not only typed.
            return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new ExpressionException($"'{literal}' is not a valid number.");
        }

        private double ParseIdentifier()
        {
            var start = _position;

            while (_position < text.Length && char.IsAsciiLetterOrDigit(text[_position]))
            {
                _position++;
            }

            var name = text[start.._position].ToLowerInvariant();

            SkipWhitespace();

            // A bare name is a constant; a name followed by '(' is a function call.
            if (!Match('('))
            {
                return name switch
                {
                    "pi" => Math.PI,
                    "e" => Math.E,
                    "tau" => Math.Tau,
                    _ => throw new ExpressionException($"Unknown name '{name}'."),
                };
            }

            var argument = ParseExpression();
            SkipWhitespace();

            if (!Match(')'))
            {
                throw new ExpressionException($"Missing a closing bracket after '{name}'.");
            }

            return name switch
            {
                "sqrt" => argument < 0
                    ? throw new ExpressionException("Cannot take the square root of a negative number.")
                    : Math.Sqrt(argument),
                "abs" => Math.Abs(argument),
                "floor" => Math.Floor(argument),
                "ceil" => Math.Ceiling(argument),
                "round" => Math.Round(argument),
                "sin" => Math.Sin(argument),
                "cos" => Math.Cos(argument),
                "tan" => Math.Tan(argument),
                "log" => argument <= 0
                    ? throw new ExpressionException("Cannot take the logarithm of a non-positive number.")
                    : Math.Log10(argument),
                "ln" => argument <= 0
                    ? throw new ExpressionException("Cannot take the logarithm of a non-positive number.")
                    : Math.Log(argument),
                _ => throw new ExpressionException($"Unknown function '{name}'."),
            };
        }

        public void ExpectEnd()
        {
            SkipWhitespace();

            if (_position < text.Length)
            {
                throw new ExpressionException($"Unexpected '{text[_position..]}'.");
            }
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private bool Match(char expected)
        {
            if (_position >= text.Length || text[_position] != expected)
            {
                return false;
            }

            _position++;
            return true;
        }
    }
}
