using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Drawing.Drawing2D;
using System.Security.Policy;
using System.Threading;
using System.Diagnostics;
using System.Security.Permissions;
using System.IO;
using System.Linq.Expressions;
using System.Net.Sockets;

namespace SlideShowApp
{
    class WebController
    {
        public static HttpListener listener;
        public static string url = "http://localhost:8000/";
        public static int pageViews = 0;
        public static int requestCount = 0;
        public static string pageData = "";
           
        public event EventHandler UpdateRequested;
        private static bool PhotoUpdateRequested;
        private CancellationTokenSource parentToken;
        private string HTMLFilePathToUse;

        public WebController(CancellationTokenSource CTS, string HTMLFilePath) 
        { 
            parentToken = CTS;
            PhotoUpdateRequested = false;

            HTMLFilePathToUse = HTMLFilePath;
        }
        public string AddQuotesIfRequired(string path)
        {
            return !string.IsNullOrWhiteSpace(path) ? path.Contains(" ") && (!path.StartsWith("\"") && !path.EndsWith("\"")) ? "\"" + path + "\"" : path : string.Empty;
        }


        public WebController() { }

        public void StartWorking()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleIncomingConnections), parentToken.Token);
        }

        public void StopWorking()
        {
            if (parentToken != null)
            {
                if (!parentToken.Token.IsCancellationRequested)
                {
                    Debug.WriteLine("Stopping listenner...");
                    // Close the listener
                    listener.Close();

                    Debug.WriteLine("Cancelling tokens...");
                    parentToken.Cancel();


                }
            }
        }
        private void HandleIncomingConnections(Object threadContext) 
        {

            CancellationToken parent = (CancellationToken)threadContext; 

            // While a user hasn't visited the `shutdown` url, keep on handling requests
            while (parent.IsCancellationRequested == false)
            {
                resetPhotoUpdateRequested();
                // Will wait here until we hear from a connection

                HttpListenerContext ctx = null;
                HttpListenerRequest req = null;
                HttpListenerResponse resp = null;
                try
                { 
                     ctx = listener.GetContext();
                }
                catch (System.Net.HttpListenerException e)
                {
                    if( e.Message.Contains ("The I/O operation has been aborted because of either a thread exit or an application request")){
                        Debug.WriteLine("HTTP Listener error occured. Assuming Parent thread is closing...");
                    } else { 
                        Console.WriteLine("Internal Error Ocurred!");
                        Console.WriteLine(e.Message);
                        Console.WriteLine(e.StackTrace);
                    }
                }


                // Peel out the requests and response objects
                if(ctx == null)
                {
                    continue;
                }

                req = ctx.Request;
                resp = ctx.Response;

                // Print out some info about the request
                Debug.WriteLine("Request #: {0}", ++requestCount);
                Debug.WriteLine(req.Url.ToString());
                Debug.WriteLine(req.HttpMethod);
                Debug.WriteLine(req.UserHostName);
                Debug.WriteLine(req.UserAgent);
                Debug.WriteLine("");

                // If `shutdown` url requested w/ POST, then shutdown the server after serving the page
                //if ((req.HttpMethod == "POST") && (req.Url.AbsolutePath == "/shutdown"))
                //{
                //    Debug.WriteLine("Shutdown requested");
                //    parentToken.Cancel();
                //    StopWorking();
                //}

                // Make sure we don't increment the page views counter if `favicon.ico` is requested
                if (req.Url.AbsolutePath != "/favicon.ico")
                    pageViews += 1;

                
                // If `RefreshImages` url requested w/ POST, then refresh images in directory
                if ((req.HttpMethod == "POST") && (req.Url.AbsolutePath == "/refreshimages"))
                {
                    Console.WriteLine("Folder Update requested");
                    PhotoUpdateRequested = true;
                    UpdateRequested.Invoke(this, new EventArgs());

                }

                if (req.Url.AbsolutePath == "/refreshimages")
                {
                    resp.Redirect(url);
                    resp.Close();
                    continue;
                }

                // If `Restart` url requested w/ POST, then restart computer
                if ((req.HttpMethod == "POST") && (req.Url.AbsolutePath == "/restart"))
                { 
                    Console.WriteLine("Shutdown requested");
                    System.Diagnostics.Process.Start("Shutdown", "-r -t 10");
                    parentToken.Cancel();
                    StopWorking();
                }

                // Write the response info 
                try
                {
                    // Open the text file using a stream reader.
                    using (var sr = new StreamReader(@HTMLFilePathToUse))
                    {
                        // Read the stream as a string, and write the string to the console. 
                        pageData = sr.ReadToEnd();
                    }

                    // Process web page and replace any $VARIABLES with code
                    pageData = pageData.Replace("$current_ip", GetLocalIPAddress());
                    pageData = pageData.Replace("$current_folder", MainProgram.filePathToUse);

                }
                catch (IOException e)
                {
                    Console.WriteLine("The HTML file could not be read:");
                    Console.WriteLine(e.Message);
                }

                byte[] data = Encoding.UTF8.GetBytes(pageData); // This is dynamically able to substiute varibles into html
                resp.ContentType = "text/html";
                resp.ContentEncoding = Encoding.UTF8;
                resp.ContentLength64 = data.LongLength;

                // Write out to the response stream (asynchronously), then close it
                resp.OutputStream.Write(data, 0, data.Length);
                resp.Close();
            }
        }
        public static string GetLocalIPAddress()
        {
            String returnString = "";
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress address in host.AddressList)
            {
                if(address.AddressFamily == AddressFamily.InterNetwork)
                { 
                    returnString += address.ToString() + " - ";
                }
            }

            return returnString;
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
        public void resetPhotoUpdateRequested()
        {
            PhotoUpdateRequested = false;
        }
        public bool isPhotoUpdateRequested()
        {
            return PhotoUpdateRequested;    
        }
        public void RunServer()
        {
            // Create a Http server and start listening for incoming connections
            listener = new HttpListener();
            PhotoUpdateRequested = false;
            listener.Prefixes.Add(url);
            listener.Start();
            Console.WriteLine("Listening for connections on {0}", url);

            // Handle requests
            StartWorking();
        } 
    }
}
