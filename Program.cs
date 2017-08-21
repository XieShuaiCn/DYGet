using System;
using System.Net;

namespace DouyuGet
{
	class MainClass
	{
		public static void Main (string[] args)
		{
            //DouyuClient client = new DouyuClient("944146");
            DouyuClient client = new DouyuClient("610588");
            client.Start();

            bool exitFlag = false;
            do
            {
                string input = Console.ReadLine().Trim().ToUpper();
                //if (input == "EXIT" || input == "QUIT")
                if (input == "")
                {
                    client.Exit();
                    exitFlag = true;
                }
            } while (!exitFlag);

		}
	}
}
