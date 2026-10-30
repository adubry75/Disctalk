namespace DisctalkClient;

public partial class App : Application
{
    public App(Core.State.ChatClient client)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        // Phone back on Wi-Fi/mobile data: reconnect now instead of waiting out the backoff.
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet) client.NotifyNetworkAvailable();
        };
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage())
        {
            Title = "Disctalk Client",
            MinimumWidth = 360,
            MinimumHeight = 480,
        };
}
