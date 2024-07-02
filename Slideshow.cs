using NewTek;
using NewTek.NDI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;

namespace SlideShowApp
{
    class Slideshow
    { 
        private List<Image> imagesToPresent;
        private int slideTransitionTimeInMs;
        private int indexToUse; 
        private System.Timers.Timer SlideShowTimer;
        private string streamName; 
        private CancellationTokenSource parentToken;
        private ManualResetEvent doneEvent;
        private bool allreadycleanedup;

        // NDI objects
        private Sender sendInstance;
        private VideoFrame videoFrame;
        private AudioFrame audioFrame;
        private Bitmap bmp;
        private Graphics graphics;

        /// <summary>
        /// Because 48kHz audio actually involves 1601.6 samples per frame at 29.97fps, we make a basic sequence that we follow.
        /// </summary>
        static int[] audioNumSamples = { 1602, 1601, 1602, 1601, 1602 };

        /// <summary>
        /// Fills the audio buffer with a test tone or silence
        /// </summary>
        /// <param name="audioFrame"></param>
        /// <param name="doTone"></param>
        static void FillAudioBuffer(AudioFrame audioFrame, bool doTone)
        {
            // should never happen
            if (audioFrame.AudioBuffer == IntPtr.Zero)
                return;

            // temp space for floats
            float[] floatBuffer = new float[audioFrame.NumSamples];

            // make the tone or silence
            double cycleLength = (double)audioFrame.SampleRate / 1000.0;
            int sampleNumber = 0;
            for (int i = 0; i < audioFrame.NumSamples; i++)
            {
                double time = sampleNumber++ / cycleLength;
                floatBuffer[i] = doTone ? (float)(Math.Sin(2.0f * Math.PI * time) * 0.1) : 0.0f;
            }

            // fill each channel with our floats...
            for (int ch = 0; ch < audioFrame.NumChannels; ch++)
            {
                // scary pointer math ahead...
                // where does this channel start in the unmanaged buffer?
                IntPtr destStart = new IntPtr(audioFrame.AudioBuffer.ToInt64() + (ch * audioFrame.ChannelStride));

                // copy the float array into the channel
                Marshal.Copy(floatBuffer, 0, destStart, audioFrame.NumSamples);
            }
        }

        // Default Slideshow
        public Slideshow(string NDIStreamName, CancellationTokenSource CTS, List<Image> SlideShowImagesToPresent) {
            streamName = NDIStreamName;
            allreadycleanedup = false;
            indexToUse = 0;
            slideTransitionTimeInMs = 5000;
             
            imagesToPresent = SlideShowImagesToPresent;
            SlideShowTimer = new System.Timers.Timer();
            SlideShowTimer.AutoReset = true;
            SlideShowTimer.Interval = slideTransitionTimeInMs;
            SlideShowTimer.Elapsed += UpdateSlide;

            parentToken = CTS;
            doneEvent = new ManualResetEvent(false);

            // When creating the sender use the Managed NDIlib Send example as the failover for this sender
            // Therefore if you run both examples and then close this one it demonstrates failover in action
            // this will show up as a source named "Example" with all other settings at their defaults
            try { 
                sendInstance = new Sender(streamName, true, false, null);
            }catch (System.AccessViolationException ae)
            {
                Console.WriteLine(ae.Message); 
                Console.WriteLine(ae.StackTrace); 

            }
            // We are going to create a 1920x1080 16:9 frame at 29.97Hz, progressive (default).
            // We are also going to create an audio frame with enough for 1700 samples for a bit of safety,
            // but 1602 should be enough using our settings as long as we don't overrun the buffer.
            // 48khz, stereo in the example.
            videoFrame = new VideoFrame(1920, 1080, (16.0f / 9.0f), 30000, 1001);
            audioFrame = new AudioFrame(1700, 48000, 2);

            // get a compatible bitmap and graphics context from our video frame.
            // also sharing a using scope.
            bmp = new Bitmap(videoFrame.Width, videoFrame.Height, videoFrame.Stride, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, videoFrame.BufferPtr);
            graphics = Graphics.FromImage(bmp);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
             
        }
        public void StartWorking()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(WorkLoop), parentToken.Token);
        }

        public void StopWorking()
        {
            if(parentToken != null)
            {
                if (! parentToken.Token.IsCancellationRequested)
                {
                    parentToken.Cancel();
                    CleanUp(); 
                }
            } 
        }
        public void CleanUp()
        {
            if (allreadycleanedup == true)
            {
                return;
            }
            if (SlideShowTimer.Enabled)
            {
                SlideShowTimer.Stop();
            }

            graphics.Dispose();
            bmp.Dispose();
            sendInstance.Dispose();
            videoFrame.Dispose();
            audioFrame.Dispose();
            doneEvent.Set(); 
            allreadycleanedup = true;
        }
        public void UpdateImageSourcesInSlideShow(List<Image> newImagesToPresent)
        {
            imagesToPresent = newImagesToPresent;
        }
        public void UpdateSlide(Object source, ElapsedEventArgs e)
        {
            Debug.WriteLine("Ticked!");
            Debug.WriteLine("The Elapsed event was raised at {0:HH:mm:ss.fff}", e.SignalTime);
            if (indexToUse >= imagesToPresent.Count - 1)
            {
                indexToUse = 0;
            }
            indexToUse++;
        }
        public void WorkLoop (Object threadContext)
        {
            Thread.CurrentThread.Name = "Slideshow Thread";
            CancellationToken parent = (CancellationToken)threadContext;

            parent.Register(() =>
            {
                Console.WriteLine("Slideshow worker thread cancellation requested!");
                CleanUp();
                return;
            });

            while (parent.IsCancellationRequested == false)
            { 
                for (int frameNumber = 0; frameNumber < 10000; frameNumber++)
                { 
                    // are we connected to anyone? 
                    if (parent.IsCancellationRequested == false && sendInstance.GetConnections(100) < 1)
                    {
                        // no point rendering
                        Debug.WriteLine("No current connections, so no rendering needed.");

                        // Wait a bit, otherwise our limited example will end before you can connect to it
                        System.Threading.Thread.Sleep(50);
                        if (SlideShowTimer.Enabled)
                        {
                            SlideShowTimer.Stop();
                        }
                    }
                    else
                    { 
                        if(parent.IsCancellationRequested)
                        {
                            return;
                        }
                        if (!SlideShowTimer.Enabled)
                        {
                            SlideShowTimer.Start();
                        }
                        // Because we are clocking to the video it is better to always submit the audio
                        // before, although there is very little in it. I'll leave it as an excercise for the
                        // reader to work out why.
                        audioFrame.NumSamples = audioNumSamples[frameNumber % 5];
                        audioFrame.ChannelStride = audioFrame.NumSamples * sizeof(float);

                        FillAudioBuffer(audioFrame, false);

                        // Submit the audio buffer
                        sendInstance.Send(audioFrame);

                        // fill it with a lovely color
                        graphics.Clear(Color.Maroon);

                        graphics.DrawImage(imagesToPresent[indexToUse], new RectangleF(0, 0, videoFrame.Width, videoFrame.Height));

                        // Get the tally state of this source (we poll it),
                        // This gets a snapshot of the current tally state.
                        // Accessing sendInstance.Tally directly would make an API call
                        // for each "if" below and could cause inaccurate results.
                        NDIlib.tally_t NDI_tally = sendInstance.Tally;

                        // We now submit the frame. Note that this call will be clocked so that we end up submitting 
                        // at exactly 29.97fps. 
                        if (videoFrame.BufferPtr != null && parent.IsCancellationRequested==false)
                            sendInstance.Send(videoFrame);

                        // Just display something helpful in the console
                        // Console.WriteLine("Frame number {0} sent.", frameNumber);
                    }
                }
            }
            CleanUp();
        }
    }
}
