using System;
using System.Reflection;
using ClientService.Helpers;
using log4net;

namespace ClientService;

internal class Program
{
	private static readonly ILog logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

	[STAThread]
    private static void Main(string[] args)
    {
        try
        {
            // ������� �� ������� �� Debug mode
            if (System.Diagnostics.Debugger.IsAttached)
            {
                // ����� �������� ��� service ���� �� ����������� ��������
                Service service = new Service("DocumentScanner", "DocumentScanner");
                service.Start(args);

                // ����� �� ��������� ������� ��� debugging
                Console.WriteLine("Press any key to stop service...");
                Console.ReadKey();

                // ������� �� service
                service.Stop();
            }
            else
            {
                // �������� ��������
                new MainCode().Main();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ReadKey(); // ����� �� �������� ������� ��� �� ���� �� error
        }
    }
}
