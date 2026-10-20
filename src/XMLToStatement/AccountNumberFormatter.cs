namespace XMLToStatement;

public enum AccountFormat
{
    Auto,
    NZ,
    AU,
    None,
}

public static class AccountNumberFormatter
{
    /// <summary>
    /// Formats an account identifier for display. In <see cref="AccountFormat.Auto"/> mode the
    /// country is taken from the servicer BIC (characters 5-6), e.g. WPACNZ2W -> NZ.
    /// </summary>
    public static string Format(string identifier, string bic, bool isIban, AccountFormat format = AccountFormat.Auto)
    {
        if (string.IsNullOrEmpty(identifier) || format == AccountFormat.None)
        {
            return identifier;
        }

        if (isIban)
        {
            return string.Join(" ", identifier.Chunk(4).Select(c => new string(c)));
        }

        if (format == AccountFormat.Auto)
        {
            var country = bic.Length >= 6 ? bic.Substring(4, 2).ToUpperInvariant() : "";
            format = country switch
            {
                "NZ" => AccountFormat.NZ,
                "AU" => AccountFormat.AU,
                _ => AccountFormat.None,
            };
        }

        if (!identifier.All(char.IsAsciiDigit))
        {
            return identifier;
        }

        return format switch
        {
            // NZ: bank(2)-branch(4)-account(7)-suffix(2 or 3)
            AccountFormat.NZ when identifier.Length is 15 or 16 =>
                $"{identifier[..2]}-{identifier[2..6]}-{identifier[6..13]}-{identifier[13..]}",
            // AU: BSB(6) as xxx-xxx followed by the account number
            AccountFormat.AU when identifier.Length > 6 =>
                $"{identifier[..3]}-{identifier[3..6]} {identifier[6..]}",
            _ => identifier,
        };
    }
}
