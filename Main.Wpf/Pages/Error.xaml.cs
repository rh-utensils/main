﻿using System.Diagnostics;
using System.Windows;
using Main.Wpf.Utilities;

namespace Main.Wpf.Pages
{
    public partial class Error
    {
        public Error()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(Config.Informations.Extension.IssueTracker);
        }
    }
}