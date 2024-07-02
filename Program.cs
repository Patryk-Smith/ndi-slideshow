using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace SlideShowApp
{
    class MainProgram
    {
        private static List<Image> imagesToPresent;
        private static string[] SUPPORTED_FILE_EXTENSIONS = { ".jpg", ".png", ".jpeg" };
        private const string REGISTRY_KEY_LOCATION = @"SOFTWARE\GFC-NDI";
        private const string REGISTRY_KEY_FILEPATH_NAME = "LastFilePathUsed";
        private static CancellationTokenSource MainCTS;
        private static CancellationTokenSource SlideCTS;
        private static Slideshow mainShow;
        public static string filePathToUse;
        private static Boolean filesUpdatedSoRefreshPresentor;
        public const string TEMPPICTUREDIR = "C:\\Public\\www\\images";
        private static string ShowDialog()
        {

            FolderBrowserDialog openFolderDialog = new FolderBrowserDialog();
            openFolderDialog.ShowNewFolderButton = true;
            DialogResult result = openFolderDialog.ShowDialog();

            if (result == DialogResult.OK)
            {
                return openFolderDialog.SelectedPath;
            }

            return string.Empty;
        }

        private static void PrintHelp()
        {

            // -s --set-directory opens the default directory
            // -o --open opens a directoyr for a single session
            Console.WriteLine("-s --set         sets the default directory to open");
            Console.WriteLine("-o --open        opens directory for this session");
            Console.WriteLine("-h --help        prints this help");
        }

        private static void SetRegistryDirectory(string FilePath)
        {
            // check if registry has saved information 
            RegistryKey key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_LOCATION);

            key.SetValue(REGISTRY_KEY_FILEPATH_NAME, FilePath);

            key.Close();

            // check if registry has saved information 
            RegistryKey newKey = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_LOCATION);
            string filePathToUse = (string)newKey.GetValue(REGISTRY_KEY_FILEPATH_NAME);
            newKey.Close();

            if (filePathToUse == null)
            {
                Console.Error.WriteLine("Cannot save file path in registry!");
            }
            if (filePathToUse != FilePath)
            {
                Console.Error.WriteLine("Cannot save file path in registry!");
            }

        }
        private static List<Image> GetImagesInDirectory(string filePathToUse)
        {
            Console.WriteLine($"Opening {filePathToUse}...");
            string[] files = Directory.GetFiles(filePathToUse); 
            List<Image> images = new List<Image>();

            // Get all supported images formats in folder from value 
            try
            {
                if (Directory.Exists(TEMPPICTUREDIR))
                {
                    Directory.Delete(TEMPPICTUREDIR, true);
                }
                Directory.CreateDirectory(TEMPPICTUREDIR);
            } catch (Exception ex){
                Console.Error.WriteLine("Cannot delete " + TEMPPICTUREDIR);
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                    
            }
            foreach (string file in files)
            {
                foreach (string extension in SUPPORTED_FILE_EXTENSIONS)
                {
                    if (file.EndsWith(extension))
                    {  
                        Console.WriteLine($"Adding {file}...");
                        Image imagetoAddd = null;
                         
                        using (FileStream stream = new FileStream(file, FileMode.Open))
                        {
                            imagetoAddd = Image.FromStream(stream);
                        } 

                        images.Add(imagetoAddd);
                    } 
                }
            }
            return images;
        }

        public static void filesUpdated(object sender, EventArgs e)
        {
            mainShow.StopWorking();
            filesUpdatedSoRefreshPresentor = true;
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length != 0)
            {

                // process args
                // -s --set-directory opens the default directory
                // -o --open opens a directoyr for a single session


                if (args.Length != 2)
                {
                    Console.Error.WriteLine("Incorrect Arguments!");
                    PrintHelp();
                }

                switch (args[0])
                {
                    case "-h":
                    case "--help":
                        PrintHelp();
                        break;

                    case "-o":
                    case "--open":
                        PrintHelp();
                        break;

                    case "-s":
                    case "--set":
                        SetRegistryDirectory(args[1]);
                        break;
                }
            }


            // used for Slideshow object
            MainCTS = new CancellationTokenSource();
            SlideCTS = new CancellationTokenSource();


            string WebPageSourceCode = "C:\\Public\\www\\index.html";
            filesUpdatedSoRefreshPresentor = false;

            // Start Webserver and subscribe to the event
            WebController WebInterface = new WebController(MainCTS, @WebPageSourceCode);
            WebInterface.UpdateRequested += new EventHandler(filesUpdated);
            WebInterface.RunServer();

            imagesToPresent = new List<Image>();

            // Load images 
            RegistryKey key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY_LOCATION);

            filePathToUse = (string)key.GetValue(REGISTRY_KEY_FILEPATH_NAME);

            key.Close();

            // Save this to registry for next use if this was just created
            if (filePathToUse == null)
            {
                Console.Error.WriteLine("No folder found in registry!");
                Console.WriteLine("Please select a folder...");

                // open folder for selection  
                while (filePathToUse == null)
                {
                    // force user to select a proper folder
                    filePathToUse = ShowDialog();
                    if (filePathToUse == null)
                    {
                        Console.Error.WriteLine("Please select a folder to open!");
                    }
                }
                // save in registry
                SetRegistryDirectory(filePathToUse);

            }
            else
            {
                Console.WriteLine("Found registry containing directoy...");
            }

            filesUpdatedSoRefreshPresentor = true;
            Console.WriteLine("Press any key to exit...");

            while (MainCTS.IsCancellationRequested == false)
            {
                if(filesUpdatedSoRefreshPresentor)
                { 
                    if( mainShow != null)
                    {
                        mainShow.StopWorking(); 
                    }

                    // filepath should NOT be null anymore
                    imagesToPresent = GetImagesInDirectory(filePathToUse);

                    SlideCTS.Dispose();

                    SlideCTS = new CancellationTokenSource(); 

                    // setup stream  
                    mainShow = new Slideshow("Building-Slideshow", SlideCTS, imagesToPresent);

                    // start streaming   
                    mainShow.StartWorking();
                    filesUpdatedSoRefreshPresentor = false;
                }
            }
            Debug.WriteLine("Stopping web interface...");
            WebInterface.StopWorking();

            Debug.WriteLine("Stopping main...");
            mainShow.StopWorking();
            Application.Exit();
        }
    }
}
