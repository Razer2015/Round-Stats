using System;
using System.Web;

/// <summary>
/// Additional Helpers
/// </summary>
public class Snippets
{
    /// <summary>
    /// Decode URL content
    /// </summary>
    /// <param name="encoded"></param>
    /// <returns></returns>
    public static String HTMLDecode(string encoded)
    {
        return (HttpUtility.HtmlDecode(encoded));
    }

    /// <summary>
    /// Encode URL content
    /// </summary>
    /// <param name="decoded"></param>
    /// <returns></returns>
	public static String HTMLEncode(string decoded)
    {
        return (HttpUtility.HtmlEncode(decoded));
    }
}