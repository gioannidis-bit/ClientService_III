using System;
using System.Reflection;
using ClientService.Classes.Factories;
using ClientService.Classes.Interfaces;
using ClientService.Helpers;
using ClientService.Models.Base;
using log4net;

namespace ClientService.Classes.Devices.AccessIS;

public class AccessISScanner : IScanner
{
	private static readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

	public AccessISCMD device;

	public IConnection connection;

	private ConfigurationModel config;

	public AccessISScanner(ConfigurationModel config)
	{
		this.config = config;
		device = new AccessISCMD();
		device.SetText = Send;
	}

	public int Connect()
	{
		try
		{
			device.Initialise();
			logger.Info("AccessIS Scanner Connected");
		}
		catch (Exception ex)
		{
			logger.Error(ex.Message);
		}
		return 0;
	}

	public int Disconnect()
	{
		try
		{
			device.Release();
		}
		catch (Exception ex)
		{
			logger.Error(ex.Message);
		}
		return 0;
	}

	public void CreateConnection()
	{
		ConnectionFactory factory = new ConcreteConnectionFactory();
		connection = factory.GetConnection(config);
	}

    public void Reconnect()
    {
        try
        {
            logger.Info("Starting scanner reconnect sequence...");

            // 1. Αποσύνδεση υπάρχουσας συσκευής
            if (device != null)
            {
                try
                {
                    // Καλούμε απευθείας το Release
                    device.Release();
                    logger.Info("Successfully released previous device connection");
                }
                catch (Exception ex)
                {
                    logger.Error($"Error releasing device: {ex.Message}", ex);
                }
                device = null;
            }

            // 2. Σημαντική παύση για να επιτρέψουμε στη συσκευή να επανέλθει
            logger.Info("Waiting for device to reset...");
            System.Threading.Thread.Sleep(500); // 500ms παύση

            // 3. Δημιουργία νέας συσκευής
            device = new AccessISCMD();
            device.SetText = Send;

            // 4. Αρχικοποίηση της νέας συσκευής - με έλεγχο επιτυχίας
            bool initSuccess = device.Initialise();

            if (initSuccess)
            {
                logger.Info("Scanner successfully reinitialized after scan.");
            }
            else
            {
                // Δοκιμάζουμε ξανά μετά από λίγο αν αποτύχει
                logger.Info("First initialization attempt failed. Trying again after delay...");
                System.Threading.Thread.Sleep(1000); // 1 δευτερόλεπτο παύση

                if (device.Initialise())
                {
                    logger.Info("Scanner successfully reinitialized on second attempt.");
                }
                else
                {
                    logger.Error("Failed to reinitialize scanner after multiple attempts!");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error in Reconnect(): {ex.Message}", ex);
        }
    }

    public string Send(string data)
    {
        if (config.Debug)
        {
            logger.Info("Sending Data: " + data);
        }
        try
        {
            if (data.ToString().StartsWith("C") && !data.ToString().ToLower().StartsWith("c<ita") && !data.ToString().ToLower().StartsWith("ca"))
            {
                new CardToCloudHelper().SendToCloud(data, config.RowSeparator, config.PostToCloudDelay ?? 20);
            }
            else
            {
                CreateConnection();
                connection.Send((int)config.RowSeparator + data);

                // Μετά το σκανάρισμα, απλά επαναφέρουμε το MSR αντί για πλήρη επανεκκίνηση
                try
                {
                    if (device != null)
                    {
                        bool resetSuccess = device.ResetMSR();
                        if (!resetSuccess)
                        {
                            logger.Warn("Αποτυχία επαναφοράς MSR μετά το σκανάρισμα - δοκιμή πλήρους επανασύνδεσης...");
                            Reconnect();
                        }
                        else
                        {
                            logger.Info("Επιτυχής επαναφορά MSR μετά το σκανάρισμα");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"Σφάλμα κατά την επαναφορά MSR: {ex.Message}", ex);
                    // Δοκιμάζουμε πλήρη επανασύνδεση αν αποτύχει το reset
                    Reconnect();
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
        }
        finally
        {
            if (connection != null)
            {
                connection.Dispose();
                connection = null;
            }
        }
        return string.Empty;
    }
}
