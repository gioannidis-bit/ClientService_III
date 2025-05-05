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

            // 1. ���������� ���������� ��������
            if (device != null)
            {
                try
                {
                    // ������� ��������� �� Release
                    device.Release();
                    logger.Info("Successfully released previous device connection");
                }
                catch (Exception ex)
                {
                    logger.Error($"Error releasing device: {ex.Message}", ex);
                }
                device = null;
            }

            // 2. ��������� ����� ��� �� ����������� ��� ������� �� ���������
            logger.Info("Waiting for device to reset...");
            System.Threading.Thread.Sleep(500); // 500ms �����

            // 3. ���������� ���� ��������
            device = new AccessISCMD();
            device.SetText = Send;

            // 4. ������������ ��� ���� �������� - �� ������ ���������
            bool initSuccess = device.Initialise();

            if (initSuccess)
            {
                logger.Info("Scanner successfully reinitialized after scan.");
            }
            else
            {
                // ����������� ���� ���� ��� ���� �� ��������
                logger.Info("First initialization attempt failed. Trying again after delay...");
                System.Threading.Thread.Sleep(1000); // 1 ������������ �����

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

                // ���� �� ����������, ���� ������������ �� MSR ���� ��� ����� ������������
                try
                {
                    if (device != null)
                    {
                        bool resetSuccess = device.ResetMSR();
                        if (!resetSuccess)
                        {
                            logger.Warn("�������� ���������� MSR ���� �� ���������� - ������ ������� �������������...");
                            Reconnect();
                        }
                        else
                        {
                            logger.Info("�������� ��������� MSR ���� �� ����������");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"������ ���� ��� ��������� MSR: {ex.Message}", ex);
                    // ����������� ����� ������������ �� �������� �� reset
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
