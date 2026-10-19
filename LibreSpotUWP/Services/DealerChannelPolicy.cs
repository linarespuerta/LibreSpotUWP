namespace LibreSpotUWP.Services
{
    internal static class DealerChannelPolicy
    {
        // ControlChannelTrigger's documented unit is minutes, range 15..1439.
        // This is OS network maintenance, not the incoming-message delivery delay.
        public const uint KeepAliveIntervalMinutes = 15;

        public static bool IsConfigurationFailure(int hresult)
        {
            return hresult == unchecked((int)0x80070057) || // E_INVALIDARG
                hresult == unchecked((int)0x80070005); // E_ACCESSDENIED
        }
    }
}
