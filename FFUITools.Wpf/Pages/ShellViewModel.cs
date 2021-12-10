using Stylet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace FFUITools.Wpf.Pages
{
    public class ShellViewModel : Conductor<IScreen>.Collection.OneActive
    {
        public static string AssemblyVersion { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        public static string AssemblyCopyright { get { return GetExecutingAssemblyAttribute<AssemblyCopyrightAttribute>(a => a.Copyright); } }
        public static string AssemblyCompany { get { return GetExecutingAssemblyAttribute<AssemblyCompanyAttribute>(a => a.Company); } }

        public ShellViewModel(MainViewModel main)
        {
            this.DisplayName = " Объединить файлы mp4";

            this.Items.Add(main);

            this.ActiveItem = main;
        }

        private static string GetExecutingAssemblyAttribute<T>(Func<T, string> value) where T : Attribute
        {
            T attribute = Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(T)) as T;
            return value.Invoke(attribute);
        }

    }
}
