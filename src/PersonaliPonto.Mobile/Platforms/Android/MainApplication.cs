using Android.App;
using Android.Runtime;

namespace PersonaliPonto.Mobile;

#if DEBUG
// Em desenvolvimento a API local roda em HTTP; em Release apenas HTTPS é permitido.
[Application(UsesCleartextTraffic = true)]
#else
[Application(UsesCleartextTraffic = false)]
#endif
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
