using System.Security.Cryptography;
using System.Text;

namespace EventCollector.Calendar;

/// <summary>
/// イベントのキーから、Google カレンダーのイベント ID を決定的に生成する。
/// 同じキーなら毎回同じ ID になるため、Insert/Update の冪等 upsert に使える。
/// </summary>
public static class CalendarEventId
{
    // Google Calendar の id は base32hex（0-9a-v）小文字・長さ5..1024 という制約があるため、
    // ハッシュバイト列をこのアルファベットでエンコードして必ず妥当な id にする。
    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    /// <summary>キー文字列から base32hex の決定的 ID を作る。</summary>
    /// <param name="key"><see cref="Models.EventItem.Key"/> 等の一意キー。</param>
    /// <returns>Google カレンダーで使える小文字 base32hex の ID。</returns>
    public static string FromKey(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Encode(hash);
    }

    // バイト列を 5bit ずつ区切って base32hex に変換する（パディングなし）。
    private static string Encode(ReadOnlySpan<byte> data)
    {
        StringBuilder sb = new(data.Length * 8 / 5 + 1);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                int index = (buffer >> bitsInBuffer) & 0b11111;
                sb.Append(Base32HexAlphabet[index]);
            }
        }

        if (bitsInBuffer > 0)
        {
            int index = (buffer << (5 - bitsInBuffer)) & 0b11111;
            sb.Append(Base32HexAlphabet[index]);
        }

        return sb.ToString();
    }
}
