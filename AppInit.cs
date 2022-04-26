using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JacRed
{
    public static class AppInit
    {
        public static List<string> proxyList;

        public static string kinozalCookie;

        public static string selezenCookie;

        public static string lostfilmCookie;

        public static (string u, string p) tolokaLogin;

        public static (string u, string p) baibakoLogin;

        public static (string u, string p) hamsterLogin;

        public static (string u, string p) animelayerLogin;

        public static void ReadConf()
        {
            try
            {
                var lines = File.ReadAllLines("Data/tr.conf");
                foreach (var line in lines)
                {
                    var args = line.Split("=");
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error parse config ${line}");
                    }
                    else
                    {
                        switch (args[0].Trim())
                        {
                            case "kinozalCookie":
                                kinozalCookie = args[1].Trim();
                                break;
                            case "selezenCookie":
                                selezenCookie = args[1].Trim();
                                break;
                            case "lostfilmCookie":
                                lostfilmCookie = args[1].Trim();
                                break;
                            case "tolokaLogin":
                                tolokaLogin.u = args[1].Trim();
                                break;
                            case "tolokaPassword":
                                tolokaLogin.p = args[1].Trim();
                                break;
                            case "baibakoLogin":
                                baibakoLogin.u = args[1].Trim();
                                break;
                            case "baibakoPassword":
                                baibakoLogin.p = args[1].Trim();
                                break;
                            case "hamsterLogin":
                                hamsterLogin.u = args[1].Trim();
                                break;
                            case "hamsterPassword":
                                hamsterLogin.p = args[1].Trim();
                                break;
                            case "animelayerLogin":
                                animelayerLogin.u = args[1].Trim();
                                break;
                            case "animelayerPassword":
                                animelayerLogin.p = args[1].Trim();
                                break;
                            case "proxy":
                                proxyList ??= new List<string>();
                                proxyList.Add(args[1].Trim());
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
