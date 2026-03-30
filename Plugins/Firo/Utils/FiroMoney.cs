using System.Globalization;

namespace BTCPayServer.Plugins.Firo.Utils
{
    public static class FiroMoney
    {
        public static decimal Convert(long satoshis)
        {
            var amt = satoshis.ToString(CultureInfo.InvariantCulture).PadLeft(8, '0');
            amt = amt.Length == 8 ? $"0.{amt}" : amt.Insert(amt.Length - 8, ".");
            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal firo)
        {
            return System.Convert.ToInt64(firo * 100000000m);
        }
    }
}
