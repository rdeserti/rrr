using rrr.Core;
using rrr.VMath;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace rrr.Scripting;

/// <summary>
/// Error in a scene script, carrying the source line number.
/// </summary>
public sealed class ScriptException : Exception
{
    public ScriptException(int line, string message)
        : base($"Line {line}: {message}")
    {
    }
}

/// <summary>
/// Typed accessor over a command's key=value arguments. Every getter takes a
/// default, so any omitted argument falls back gracefully; malformed values
/// throw a <see cref="ScriptException"/> with the line number.
/// </summary>
public sealed class ScriptArguments
{
    private readonly Dictionary<string, string> _values;
    private readonly int _line;

    public int Line => _line;

    public ScriptArguments(Dictionary<string, string> values, int line)
    {
        _values = values;
        _line = line;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    public bool Any => _values.Count > 0;

    public string GetString(string key, string fallback)
    {
        return _values.TryGetValue(key, out string? v) ? v : fallback;
    }

    public float GetFloat(string key, float fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        return ParseFloat(v, key);
    }

    public int GetInt(string key, int fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
            throw new ScriptException(_line, $"'{key}' is not an integer: '{v}'.");

        return r;
    }

    public bool GetBool(string key, bool fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        switch (v.ToLowerInvariant())
        {
            case "on":
            case "true":
            case "yes":
            case "1":
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                return false;
            default:
                throw new ScriptException(_line, $"'{key}' is not a boolean: '{v}'.");
        }
    }

    public Vector3f GetVector3(string key, Vector3f fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        string[] p = v.Split(',');

        if (p.Length != 3)
            throw new ScriptException(_line, $"'{key}' expects x,y,z: '{v}'.");

        return new Vector3f(
            ParseFloat(p[0], key),
            ParseFloat(p[1], key),
            ParseFloat(p[2], key));
    }

    public Vector2f GetVector2(string key, Vector2f fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        string[] p = v.Split(',');

        if (p.Length != 2)
            throw new ScriptException(_line, $"'{key}' expects x,y: '{v}'.");

        return new Vector2f(ParseFloat(p[0], key), ParseFloat(p[1], key));
    }

    /// <summary>
    /// Scale value: either a single number (uniform) or x,y,z.
    /// </summary>
    public Vector3f GetScale(string key, Vector3f fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        if (!v.Contains(','))
        {
            float s = ParseFloat(v, key);
            return new Vector3f(s, s, s);
        }

        return GetVector3(key, fallback);
    }

    public ColorRGBAf GetColor(string key, ColorRGBAf fallback)
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        if (v.StartsWith("#"))
        {
            string h = v.Substring(1);

            if (h.Length != 6)
                throw new ScriptException(_line, $"'{key}' hex color must be #rrggbb: '{v}'.");

            try
            {
                float r = Convert.ToInt32(h.Substring(0, 2), 16) / 255.0f;
                float g = Convert.ToInt32(h.Substring(2, 2), 16) / 255.0f;
                float b = Convert.ToInt32(h.Substring(4, 2), 16) / 255.0f;
                return new ColorRGBAf(r, g, b);
            }
            catch
            {
                throw new ScriptException(_line, $"'{key}' invalid hex color: '{v}'.");
            }
        }

        string[] p = v.Split(',');

        if (p.Length == 3)
            return new ColorRGBAf(
                ParseFloat(p[0], key), ParseFloat(p[1], key), ParseFloat(p[2], key));

        if (p.Length == 4)
            return new ColorRGBAf(
                ParseFloat(p[0], key), ParseFloat(p[1], key),
                ParseFloat(p[2], key), ParseFloat(p[3], key));

        throw new ScriptException(_line, $"'{key}' expects r,g,b or r,g,b,a or #rrggbb: '{v}'.");
    }

    public T GetEnum<T>(string key, T fallback) where T : struct, Enum
    {
        if (!_values.TryGetValue(key, out string? v))
            return fallback;

        if (!Enum.TryParse(v, ignoreCase: true, out T result))
            throw new ScriptException(_line, $"'{key}' invalid value '{v}' for {typeof(T).Name}.");

        return result;
    }

    private float ParseFloat(string v, string key)
    {
        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
            throw new ScriptException(_line, $"'{key}' is not a number: '{v}'.");

        return r;
    }
}
