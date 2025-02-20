﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DarkUI.Forms;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using RestSharp;
using Serilog;

namespace uploader
{
    public partial class UploadForm : DarkForm
    {
        private readonly bool _reopen;
        private readonly string _fileName;
        private readonly MainForm _mainForm;
        private readonly Settings _settings;
        private Thread _uploadThread;
        private RestClient _client;

        public UploadForm(MainForm mainForm, Settings settings, bool reopen, string fileName)
        {
            _fileName = fileName;
            _mainForm = mainForm;
            _settings = settings;
            _reopen = reopen;
            
            InitializeComponent();
        }

        private void ChangeStatus(string text)
        {
            Log.Logger.Verbose($"Changed status to {text}");
            if (InvokeRequired)
            {
                this.Invoke(new Action(() => ChangeStatus(text)));
                return;
            }

            statusLabel.Text = text;
        }

        private void Finish(bool resetText)
        {
            Log.Logger.Verbose($"Finished uplaod");
            if (InvokeRequired)
            {
                this.Invoke(new Action(() => Finish(resetText)));
                return;
            }

            if (resetText)
            {
                ChangeStatus(LocalizationHelper.Base.Message_Idle);
            }

            uploadButton.Text = LocalizationHelper.Base.UploadForm_Upload;
        }

        private void CloseWindow()
        {
            Log.Logger.Verbose($"Upload window closed");
            if (InvokeRequired)
            {
                this.Invoke(new Action(() => CloseWindow()));
                return;
            }

            this.Close();
        }

        private void Upload()
        {
            Log.Logger.Information($"Lookup start");
            if (string.IsNullOrEmpty(_settings.ApiKey))
            {
                Log.Logger.Warning(LocalizationHelper.Base.UploadForm_NoApiKey);
                MessageBox.Show(LocalizationHelper.Base.UploadForm_NoApiKey, LocalizationHelper.Base.UploadForm_InvalidKey, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_settings.ApiKey.Length != 64)
            {
                Log.Logger.Warning(LocalizationHelper.Base.UploadForm_InvalidLength);
                MessageBox.Show(LocalizationHelper.Base.UploadForm_InvalidLength, LocalizationHelper.Base.UploadForm_InvalidKey, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            ChangeStatus(LocalizationHelper.Base.Message_Init);
            _client = new RestClient("https://www.virustotal.com");

            if (!File.Exists(_fileName))
            {
                throw new FileNotFoundException();
            }

            ChangeStatus(LocalizationHelper.Base.Message_Check);
            var reportRequest = new RestRequest("vtapi/v2/file/report", Method.POST);
            reportRequest.AddParameter("apikey", _settings.ApiKey);
            reportRequest.AddParameter("resource", Utils.GetMD5(_fileName));

            var reportResponse = _client.Execute(reportRequest);
            var reportContent = reportResponse.Content;
            dynamic reportJson = JsonConvert.DeserializeObject(reportContent);

            try
            {
                var reportLink = reportJson.permalink.ToString();
                Process.Start(reportLink);
                Log.Logger.Information($"Lookup finished");

                if (_settings.DirectUpload) CloseWindow();
            }
            catch (RuntimeBinderException ex1)
            {
                Log.Logger.Debug(ex1, "Exception expected on lookup failure");
                Log.Logger.Information($"Lookup failed");
                Log.Logger.Information($"Upload start");
                // Json does not contain permalink which means it's a new file (or the request failed)
                ChangeStatus(LocalizationHelper.Base.Message_Upload);
                var scanRequest = new RestRequest("vtapi/v2/file/scan", Method.POST);
                scanRequest.AddParameter("apikey", _settings.ApiKey);
                scanRequest.AddFile("file", _fileName);

                var scanResponse = _client.Execute(scanRequest);
                var scanContent = scanResponse.Content;
                // TODO: check for HTML (file too large)
                dynamic scanJson = JsonConvert.DeserializeObject(scanContent);

                try
                {
                    string scanLink = scanJson.permalink.ToString();

                    // An example link can look like this:
                    // https://www.virustotal.com/gui/file/<filehash_or_resource_id>/detection/<scanid>
                    // If we don't remove the the scanid, then it will fail on new files since the scan did not finish
                    // Removing it like this will show the analysis progress for new files
                    scanLink = scanLink.Remove(scanLink.IndexOf("/detection"));
                    
                    Process.Start(scanLink);
                    Log.Logger.Information($"Upload finished");

                    if (_settings.DirectUpload) CloseWindow();
                }
                catch (Exception ex2)
                {
                    Log.Logger.Error(ex2, "Excpetion while uploading file to TV");
                    Log.Logger.Information($"Upload failed");
                    // Response does not contain permalink so it failed
                    ChangeStatus(LocalizationHelper.Base.Message_NoLink);
                    Finish(false);
                    return;
                }
            }

            Finish(true);
        }

        private void StartUploadThread()
        {
            if (_uploadThread != null && _uploadThread.IsAlive)
            {
                _uploadThread.Abort();
                uploadButton.Text = LocalizationHelper.Base.UploadForm_Upload;
                return;
            }
            uploadButton.Text = LocalizationHelper.Base.UploadForm_Cancel;

            _uploadThread = new Thread(Upload);
            _uploadThread.Start();
        }

        private void UploadForm_Load(object sender, EventArgs e)
        {
            mdTextbox.Text = Utils.GetMD5(_fileName);
            shaTextbox.Text = Utils.GetSHA1(_fileName);
            sha2Textbox.Text = Utils.GetSHA256(_fileName);

            settingsGroup.Text = LocalizationHelper.Base.UploadForm_Info;
            uploadButton.Text = LocalizationHelper.Base.UploadForm_Upload;
            statusLabel.Text = LocalizationHelper.Base.Message_Idle;

            if (_settings.DirectUpload)
            {
                StartUploadThread();
            }
        }

        private void uploadButton_Click(object sender, EventArgs e)
        {
            StartUploadThread();
        }

        private void UploadForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_reopen)
            {
                _mainForm.Show();
            }
            else
            {
                _mainForm.Close();
            }
        }
    }
}
