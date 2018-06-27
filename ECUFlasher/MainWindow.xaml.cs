/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2017  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: tony@nefariousmotorsports.com
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Runtime.InteropServices;

using Shared;

namespace ECUFlasher
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			App = (App)Application.Current;
            Title = App.GetApplicationName();

            InitializeComponent();

			Loaded += OnLoaded;
		}

		void OnLoaded(object sender, RoutedEventArgs e)
		{
			Window parentWindow = Window.GetWindow(this);

			if (parentWindow != null)
			{
				parentWindow.Closing += delegate(object closingSender, CancelEventArgs closingArgs)
				{
					if ((App != null) && App.OperationInProgress)
					{
						closingArgs.Cancel = (App.DisplayUserPrompt("Operation in Progress", "An operation is in progress. Press OK to abort the operation and quit, or Cancel otherwise.", UserPromptType.OK_CANCEL) == UserPromptResult.CANCEL);
					}
				};
			}
		}

		public App App { get; private set; }
	}    
}

internal static class NativeMethods
{
    // Import SetThreadExecutionState Win32 API and necessary flags
    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint esFlags);
    public const uint ES_AWAYMODE_REQUIRED =    0x00000040;
    public const uint ES_CONTINUOUS =           0x80000000;
    public const uint ES_SYSTEM_REQUIRED =      0x00000001;    
    public const uint ES_DISPLAY_REQUIRED =     0x00000002;
    public const uint ES_USER_PRESENT =         0x00000004;
}
