using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using log4net;

namespace ClientService.Classes.Devices.AccessIS;

public class AccessISCMD : IDisposable
{
	private delegate void updateTextBox(string text);

	private delegate void updateDisplayMrz(int status);

	private delegate void updatedReaderButtonStatus(bool enableReader, bool disableReader);

	private delegate void disableButtons();

	private delegate void msrDelegate(ref uint Parameter, [MarshalAs(UnmanagedType.LPStr)] StringBuilder data, int dataSize);

	private delegate void msrConnectionDelegate(ref uint Parameter, bool connectionStatus);

       
    private enum PacketType
	{
		MRZ,
		MSR
	}

	private static readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

	private msrDelegate msrData;

	private msrConnectionDelegate msrDataConnection;

	private const string DLL_LOCATION = "Access_IS_MSR.dll";

	public Func<string, string> SetText { get; set; }

	[DllImport(DLL_LOCATION, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
	private static extern void initialiseMsr(bool managedCode);

    [DllImport("Access_IS_MSR.dll",
    EntryPoint = "msrRelease",     // ή "releaseMsr" αν θέλεις να στοχεύσεις αυτό
    CallingConvention = CallingConvention.StdCall)]
    private static extern void msrRelease();

    [DllImport(DLL_LOCATION, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern bool enableMSR();

	[DllImport(DLL_LOCATION, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern bool disableMSR();

	[DllImport(DLL_LOCATION, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern PacketType getPacketType();

    [DllImport(DLL_LOCATION,
      EntryPoint = "getDeviceName",
      CharSet = CharSet.Ansi,
      CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr getDeviceName();


    public static string DeviceName
    {
        get
        {
            IntPtr p = getDeviceName();            // calls into the DLL
            if (p == IntPtr.Zero) return string.Empty;
            return Marshal.PtrToStringAnsi(p)!;    // ANSI-to-managed
        }
    }



    [DllImport("Access_IS_MSR.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
	private static extern int getMrzFailureStatus();

	[DllImport("Access_IS_MSR.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern bool registerMSRCallback(msrDelegate Callback, ref uint Parameter);

	[DllImport("Access_IS_MSR.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
	private static extern bool registerMSRConnectionCallback(msrConnectionDelegate Callback, ref uint Parameter);


    /// <summary>
    /// Επιστρέφει true αν μάζεψε όνομα από τη native DLL, false αν όχι.
    /// </summary>
    public static bool IsDeviceConnected()
    {
        // Βεβαιώσου ότι έχεις καλέσει initialiseMsr() νωρίτερα
        IntPtr ptr = getDeviceName();
        if (ptr == IntPtr.Zero)
            return false;

        string name = Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(name);
    }


    public bool Initialise()
    {
        try
        {
            uint Val = 0u;
            initialiseMsr(managedCode: true);

            // Έλεγχος αν η συσκευή είναι συνδεδεμένη
            if (IsDeviceConnected())
            {
                string devName = DeviceName;
                logger.Info($"[AccessISCMD] Συσκευή συνδεδεμένη: {devName}");

                // Καταχώρηση callbacks μόνο αν η συσκευή είναι συνδεδεμένη
                msrData = MsrCallback;
                msrDataConnection = MsrConnectionCallback;
                registerMSRCallback(msrData, ref Val);
                registerMSRConnectionCallback(msrDataConnection, ref Val);

                // Ενεργοποίηση MSR
                bool msrEnabled = enableMSR();
                if (msrEnabled)
                {
                    logger.Info("[AccessISCMD] MSR ενεργοποιήθηκε επιτυχώς");
                }
                else
                {
                    logger.Warn("[AccessISCMD] Αποτυχία ενεργοποίησης MSR!");
                }

                return true;
            }
            else
            {
                logger.Warn("[AccessISCMD] Δεν βρέθηκε συνδεδεμένη συσκευή!");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[AccessISCMD] Σφάλμα κατά την αρχικοποίηση: {ex.Message}", ex);
            return false;
        }
    }

    public void Release()
    {
        uint val = 0;

        try
        {
            // 1) Απενεργοποιούμε πρώτα το MSR
            try
            {
                disableMSR();
                logger.Info("[AccessISCMD] MSR απενεργοποιήθηκε");
            }
            catch (Exception ex)
            {
                logger.Warn($"[AccessISCMD] Σφάλμα στο disableMSR(): {ex.Message}", ex);
            }

            // 2) Απο-εγγράφουμε τους callbacks
            try
            {
                registerMSRCallback(null, ref val);
                registerMSRConnectionCallback(null, ref val);
                logger.Info("[AccessISCMD] Callbacks απο-εγγράφηκαν");
            }
            catch (Exception ex)
            {
                logger.Warn($"[AccessISCMD] Σφάλμα στην απο-εγγραφή callbacks: {ex.Message}", ex);
            }

            // 3) Καλούμε τη συνάρτηση απελευθέρωσης πόρων
            try
            {
                msrRelease();
                logger.Info("[AccessISCMD] Πόροι απελευθερώθηκαν επιτυχώς");
            }
            catch (Exception ex)
            {
                logger.Warn($"[AccessISCMD] Σφάλμα στο msrRelease(): {ex.Message}", ex);
            }

            // 4) Καθαρίζουμε τα managed references
            msrData = null;
            msrDataConnection = null;
        }
        catch (Exception ex)
        {
            logger.Error($"[AccessISCMD] Γενικό σφάλμα στο Release(): {ex.Message}", ex);
        }
    }


    // Στο αρχείο AccessISCMD.cs προσθέτουμε μια νέα μέθοδο
    public bool ResetMSR()
    {
        try
        {
            logger.Info("[AccessISCMD] Επαναφορά MSR - ξεκίνησε");

            // 1. Απενεργοποίηση MSR
            bool disableSuccess = disableMSR();
            if (!disableSuccess)
            {
                logger.Warn("[AccessISCMD] Αποτυχία απενεργοποίησης MSR");
            }

            // 2. Μικρή καθυστέρηση
            System.Threading.Thread.Sleep(200);

            // 3. Επανενεργοποίηση MSR
            bool enableSuccess = enableMSR();
            if (!enableSuccess)
            {
                logger.Warn("[AccessISCMD] Αποτυχία ενεργοποίησης MSR");
                return false;
            }

            logger.Info("[AccessISCMD] Επαναφορά MSR - ολοκληρώθηκε επιτυχώς");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error($"[AccessISCMD] Σφάλμα κατά την επαναφορά MSR: {ex.Message}", ex);
            return false;
        }
    }



    private void MsrCallback(ref uint Parameter, [MarshalAs(UnmanagedType.LPStr)] StringBuilder data, int dataSize)
	{
		logger.Info("AccessIS listener triggered");
		SetText(data.ToString());
	}

	private void MsrConnectionCallback(ref uint Parameter, bool connectionStatus)
	{
	}

    public void Dispose()
    {
        try
        {
            // Απλά καλούμε Release αντί για Reconnect
            Release();
            logger.Info("[AccessISCMD] Device resources released");
        }
        catch (Exception ex)
        {
            logger.Error($"[AccessISCMD] Error in Dispose(): {ex.Message}", ex);
        }
    }

    ~AccessISCMD()
	{
	}
}
