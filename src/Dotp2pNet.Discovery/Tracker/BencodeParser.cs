using System.Text;

namespace Dotp2pNet.Discovery.Tracker;

/// <summary>
/// Parses bencoded data used in BitTorrent protocol.
/// </summary>
/// <remarks>
/// Bencoding is a simple encoding format used by BitTorrent for tracker responses.
/// Format:
/// - Integers: i[number]e (e.g., i42e = 42)
/// - Strings: [length]:[string] (e.g., 4:spam = "spam")
/// - Lists: l[elements]e (e.g., l4:spam4:eggse = ["spam", "eggs"])
/// - Dictionaries: d[key][value]...e (e.g., d3:cow3:moo4:spam4:eggse = {"cow": "moo", "spam": "eggs"})
/// </remarks>
internal static class BencodeParser
{
    /// <summary>
    /// Parses a bencoded byte array into a dictionary.
    /// </summary>
    /// <param name="data">The bencoded data.</param>
    /// <returns>A dictionary representing the parsed data.</returns>
    /// <exception cref="FormatException">Thrown when the data is not valid bencoded format.</exception>
    public static Dictionary<string, object> ParseDictionary(byte[] data)
    {
        int index = 0;
        var result = ParseValue(data, ref index);
        
        if (result is not Dictionary<string, object> dict)
        {
            throw new FormatException("Expected dictionary at root level");
        }

        return dict;
    }

    private static object ParseValue(byte[] data, ref int index)
    {
        if (index >= data.Length)
        {
            throw new FormatException("Unexpected end of data");
        }

        char type = (char)data[index];

        return type switch
        {
            'i' => ParseInteger(data, ref index),
            'l' => ParseList(data, ref index),
            'd' => ParseDictionary(data, ref index),
            >= '0' and <= '9' => ParseString(data, ref index),
            _ => throw new FormatException($"Unknown bencode type: {type}")
        };
    }

    private static long ParseInteger(byte[] data, ref int index)
    {
        // Format: i[number]e
        index++; // Skip 'i'

        int start = index;
        while (index < data.Length && data[index] != 'e')
        {
            index++;
        }

        if (index >= data.Length)
        {
            throw new FormatException("Unterminated integer");
        }

        string numberStr = Encoding.ASCII.GetString(data, start, index - start);
        index++; // Skip 'e'

        if (!long.TryParse(numberStr, out long result))
        {
            throw new FormatException($"Invalid integer: {numberStr}");
        }

        return result;
    }

    private static byte[] ParseString(byte[] data, ref int index)
    {
        // Format: [length]:[string]
        int start = index;
        while (index < data.Length && data[index] != ':')
        {
            index++;
        }

        if (index >= data.Length)
        {
            throw new FormatException("Unterminated string length");
        }

        string lengthStr = Encoding.ASCII.GetString(data, start, index - start);
        index++; // Skip ':'

        if (!int.TryParse(lengthStr, out int length))
        {
            throw new FormatException($"Invalid string length: {lengthStr}");
        }

        if (index + length > data.Length)
        {
            throw new FormatException("String length exceeds data bounds");
        }

        byte[] result = new byte[length];
        Array.Copy(data, index, result, 0, length);
        index += length;

        return result;
    }

    private static List<object> ParseList(byte[] data, ref int index)
    {
        // Format: l[elements]e
        index++; // Skip 'l'

        var list = new List<object>();

        while (index < data.Length && data[index] != 'e')
        {
            list.Add(ParseValue(data, ref index));
        }

        if (index >= data.Length)
        {
            throw new FormatException("Unterminated list");
        }

        index++; // Skip 'e'
        return list;
    }

    private static Dictionary<string, object> ParseDictionary(byte[] data, ref int index)
    {
        // Format: d[key][value]...e
        index++; // Skip 'd'

        var dict = new Dictionary<string, object>();

        while (index < data.Length && data[index] != 'e')
        {
            // Keys must be strings
            byte[] keyBytes = ParseString(data, ref index);
            string key = Encoding.UTF8.GetString(keyBytes);

            // Parse value
            object value = ParseValue(data, ref index);

            dict[key] = value;
        }

        if (index >= data.Length)
        {
            throw new FormatException("Unterminated dictionary");
        }

        index++; // Skip 'e'
        return dict;
    }

    /// <summary>
    /// Gets a string value from a dictionary.
    /// </summary>
    public static string? GetString(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string str => str,
            _ => null
        };
    }

    /// <summary>
    /// Gets a byte array value from a dictionary.
    /// </summary>
    public static byte[]? GetBytes(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as byte[];
    }

    /// <summary>
    /// Gets an integer value from a dictionary.
    /// </summary>
    public static long? GetInteger(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as long?;
    }

    /// <summary>
    /// Gets a list value from a dictionary.
    /// </summary>
    public static List<object>? GetList(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as List<object>;
    }

    /// <summary>
    /// Gets a dictionary value from a dictionary.
    ///   /// </s>
    public static Dictionary<string, object>? GetDictionary(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as Dictionary<string, object>;
    }
}