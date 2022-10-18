using HideVolumeOSD.Properties;
using OSVersionExtension;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace HideVolumeOSD
{
    public class HideVolumeOSDLib
    {
        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

            [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        NotifyIcon ni;

        IntPtr hWndInject = IntPtr.Zero;

        private string _lpszClassHost;
        private string _lpszClassChild;
        private string _lpszTitleChild;
        private const string _lpszClassChild2 = "Windows.UI.Input.InputSite.WindowClass";
        private bool _isWindows11;

        public HideVolumeOSDLib(NotifyIcon ni)
        {
            this.ni = ni;
        }


        public void Init()
        {
            _isWindows11 = OSVersion.GetOSVersion().Version.Build >= 22000;// Win 11 build >=22k

            if (_isWindows11) //Win11 
            {
                _lpszClassHost = "XamlExplorerHostIslandWindow"; //Win11 top window
                _lpszClassChild = "Windows.UI.Composition.DesktopWindowContentBridge"; //Win11 1st child
                _lpszTitleChild = "DesktopWindowXamlSource"; //Win11 1st child title
            }
            else
            {
                _lpszClassHost = "NativeHWNDHost"; //Win11 XamlExplorerHostIslandWindow
                _lpszClassChild = "DirectUIHWND"; //Win11 Windows.UI.Composition.DesktopWindowContentBridge
                _lpszTitleChild = ""; //Win11 DesktopWindowXamlSource
            }

            hWndInject = FindOSDWindow(true);

            int count = 1;

            while (hWndInject == IntPtr.Zero && count < 9)
            {
                ShowVolumeWindow();

                hWndInject = FindOSDWindow(true);

                // Quadratic backoff if the window is not found
                System.Threading.Thread.Sleep(1000 * (count ^ 2));
                count++;
            }

            // final try

            hWndInject = FindOSDWindow(false);

            if (hWndInject == IntPtr.Zero)
            {
                Program.InitFailed = true;
                return;
            }

            if (ni != null)
            {
                if (Settings.Default.HideOSD)
                    HideOSD();
                else
                    ShowOSD();

                Application.ApplicationExit += Application_ApplicationExit;
            }
        }
        private IntPtr FindOSDWindow(bool bSilent)
        {
            IntPtr hwndRet = IntPtr.Zero;
            IntPtr hwndHost = IntPtr.Zero;
            IntPtr hwndChild = IntPtr.Zero;
            IntPtr hwndChild2 = IntPtr.Zero;
            int pairCount = 0;

            // search for all windows with class 'NativeHWNDHost'

            while ((hwndHost = FindWindowEx(IntPtr.Zero, hwndHost, _lpszClassHost, "")) != IntPtr.Zero)
            {
                // if this window has a child with class 'DirectUIHWND' it might be the volume OSD

                hwndChild = FindWindowEx(hwndHost, IntPtr.Zero, _lpszClassChild, _lpszTitleChild);
                if (_isWindows11 && hwndChild != IntPtr.Zero) //Win11 additional checks to find proper OSD
                {
                    //check for child's child in Win11 w class "Windows.UI.Input.InputSite.WindowClass"
                    hwndChild2 = FindWindowEx(hwndChild, IntPtr.Zero, _lpszClassChild2, "");
                    if (hwndChild2 != IntPtr.Zero)
                    {
                        ShowWindow(hwndHost, 9); // SW_RESTORE any previously minimized OSD windows from this app so we can look at the proper restored Client Window Rect and see if it is the OSD or other zero sized windows. Skipping style check for SW_MINIMIZED, maybe l8r
                        if (GetClientRect(hwndHost, out RECT rect))
                        {
                            if (rect.Top == 0 && rect.Left == 0 && rect.Bottom == 0 && rect.Right == 0) //Windows 11 other windows with same classes etc but zero size so a 4th check is needed. These may be Widget hosts or other. Test for zero RECT size and exclude from paircount if zero; this is not the OSD you're looking for. 
                            {
                                hwndChild = IntPtr.Zero;
                            }
                        }
                    }
                    else
                    {
                        hwndChild = IntPtr.Zero; //2nd child window didn't match or was zero size anomaly for Win11, this is not the window we're looking for
                    }

                }

                if (hwndChild != IntPtr.Zero)//we have a match
                {
                    // if this is the only pair we are sure
                    if (pairCount == 0)
                    {
                        hwndRet = hwndHost;
                    }

                    pairCount++;

                    // if there are more pairs the criteria has failed...

                    if (pairCount > 1)
                    {
                        MessageBox.Show("Severe error: Multiple pairs found!\nTry restarting the Windows Desktop's Explorer.exe process or Sign Out and try again", "HideVolumeOSD", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return IntPtr.Zero;
                    }
                }
            }

            // if no window found yet, there is no OSD window at all

            if (hwndRet == IntPtr.Zero && !bSilent)
            {
                MessageBox.Show("Severe error: OSD window not found!", "HideVolumeOSD");
            }

            return hwndRet;
        }

        private void Application_ApplicationExit(object sender, EventArgs e)
        {
            ShowOSD();
        }

        public void HideOSD()
        {
            if (!IsWindow(hWndInject))
            {
                Init();
            }

            //SW_HIDE 0, SW_MINIMIZE 6
            ShowWindow(hWndInject, 6); // SW_MINIMIZE

            if (ni != null)
                ni.Icon = Resources.IconDisabled;
        }

        public void ShowOSD()
        {
            if (!IsWindow(hWndInject))
            {
                Init();
            }

            //SW_SHOW 5, SW_RESTORE 6
            ShowWindow(hWndInject, 9); // SW_RESTORE

            // show window on the screen

            ShowVolumeWindow();
            if (ni != null)
                ni.Icon = Resources.Icon;
        }

        private static void ShowVolumeWindow()
        {
            keybd_event((byte)Keys.VolumeMute, 0, 0, 0);
            System.Threading.Thread.Sleep(100);
            keybd_event((byte)Keys.VolumeMute, 0, 0, 0);
        }
    }
}
