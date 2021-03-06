﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Threading;
using System.Media;
using NAudio.Wave;
using System.Diagnostics;

namespace JNSoundboard
{
    public partial class MainForm : Form
    {
        WaveIn loopbackSourceStream = null;
        BufferedWaveProvider loopbackWaveProvider = null;
        WaveOut loopbackWaveOut = null;
        WaveOut playbackWaveOut = null;

        Random rand = new Random();

        bool keyUpPushToTalkKey = false;

        internal List<XMLSettings.KeysSounds> keysSounds = new List<XMLSettings.KeysSounds>();
        Keys pushToTalkKey;

        internal string xmlLoc = "";

        public MainForm()
        {
            InitializeComponent();

            var tooltip = new ToolTip();
            tooltip.SetToolTip(btnReloadDevices, "Refresh sound devices");
            tooltip.SetToolTip(btnReloadWindows, "Reload windows");

            loadSoundDevices();
            loadWindows();

            XMLSettings.LoadSoundboardSettingsXML();

            if (cbPlaybackDevices.Items.Contains(XMLSettings.soundboardSettings.LastPlaybackDevice)) cbPlaybackDevices.SelectedItem = XMLSettings.soundboardSettings.LastPlaybackDevice;

            if (cbLoopbackDevices.Items.Contains(XMLSettings.soundboardSettings.LastLoopbackDevice)) cbLoopbackDevices.SelectedItem = XMLSettings.soundboardSettings.LastLoopbackDevice;

            //add events after settings have been loaded
            cbPlaybackDevices.SelectedIndexChanged += cbPlaybackDevices_SelectedIndexChanged;
            cbLoopbackDevices.SelectedIndexChanged += cbLoopbackDevices_SelectedIndexChanged;
        }

        private void loadWindows()
        {
            cbWindows.Items.Clear();

            cbWindows.Items.Add("");

            Process[] processlist = Process.GetProcesses();

            foreach (Process process in processlist)
            {
                if (!string.IsNullOrEmpty(process.MainWindowTitle))
                {
                    cbWindows.Items.Add(process.MainWindowTitle);
                }
            }
        }

        private void loadSoundDevices()
        {
            var playbackSources = new List<WaveOutCapabilities>();
            var loopbackSources = new List<WaveInCapabilities>();

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                playbackSources.Add(WaveOut.GetCapabilities(i));
            }

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                loopbackSources.Add(WaveIn.GetCapabilities(i));
            }

            cbPlaybackDevices.Items.Clear();
            cbLoopbackDevices.Items.Clear();

            foreach (var source in playbackSources)
            {
                cbPlaybackDevices.Items.Add(source.ProductName);
            }

            if (cbPlaybackDevices.Items.Count > 0)
                cbPlaybackDevices.SelectedIndex = 0;

            cbLoopbackDevices.Items.Add("");

            foreach (var source in loopbackSources)
            {
                cbLoopbackDevices.Items.Add(source.ProductName);
            }

            cbLoopbackDevices.SelectedIndex = 0;
        }

        private void startLoopback()
        {
            stopLoopback();

            int deviceNumber = cbLoopbackDevices.SelectedIndex - 1;

            if (loopbackSourceStream == null)
                loopbackSourceStream = new WaveIn();
            loopbackSourceStream.DeviceNumber = deviceNumber;
            loopbackSourceStream.WaveFormat = new WaveFormat(44100, WaveIn.GetCapabilities(deviceNumber).Channels);
            loopbackSourceStream.BufferMilliseconds = 25;
            loopbackSourceStream.NumberOfBuffers = 5;
            loopbackSourceStream.DataAvailable += loopbackSourceStream_DataAvailable;

            loopbackWaveProvider = new BufferedWaveProvider(loopbackSourceStream.WaveFormat);
            loopbackWaveProvider.DiscardOnBufferOverflow = true;

            if (loopbackWaveOut == null)
                loopbackWaveOut = new WaveOut();
            loopbackWaveOut.DeviceNumber = cbPlaybackDevices.SelectedIndex;
            loopbackWaveOut.DesiredLatency = 125;
            loopbackWaveOut.Init(loopbackWaveProvider);

            loopbackSourceStream.StartRecording();
            loopbackWaveOut.Play();
        }

        private void stopLoopback()
        {
            try
            {
                if (loopbackWaveOut != null)
                {
                    loopbackWaveOut.Stop();
                    loopbackWaveOut.Dispose();
                    loopbackWaveOut = null;
                }

                if (loopbackWaveProvider != null)
                {
                    loopbackWaveProvider.ClearBuffer();
                    loopbackWaveProvider = null;
                }

                if (loopbackSourceStream != null)
                {
                    loopbackSourceStream.StopRecording();
                    loopbackSourceStream.Dispose();
                    loopbackSourceStream = null;
                }
            }
            catch (Exception) { }
        }

        private void stopPlayback()
        {
            try
            {
                if (playbackWaveOut != null && playbackWaveOut.PlaybackState == PlaybackState.Playing)
                {
                    playbackWaveOut.Stop();
                    playbackWaveOut.Dispose();
                    playbackWaveOut = null;
                }
            }
            catch (Exception) { }
        }

        private void playSound(string file)
        {
            int deviceNumber = cbPlaybackDevices.SelectedIndex;

            if (!XMLSettings.soundboardSettings.PlaySoundsOverEachOther) stopPlayback();

            if (playbackWaveOut == null) playbackWaveOut = new WaveOut();

            playbackWaveOut.DeviceNumber = deviceNumber;

            try
            {
                playbackWaveOut.Init(new AudioFileReader(file));

                playbackWaveOut.Play();
            }
            catch (FormatException ex)
            {
                SystemSounds.Beep.Play();
                MessageBox.Show(ex.ToString());
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                SystemSounds.Beep.Play();
                MessageBox.Show(ex.ToString());
            }
            catch (NAudio.MmException ex)
            {
                SystemSounds.Beep.Play();
                string msg = ex.ToString();
                MessageBox.Show((msg.Contains("UnspecifiedError calling waveOutOpen") ? "Something is wrong with either the sound you tried to play (" + file.Substring(file.LastIndexOf("\\") + 1) + ") (try converting it to another format) or your sound card driver\n\n" + msg : msg));
            }
        }

        private void loadXMLFile(string path)
        {
            XMLSettings.Settings s = (XMLSettings.Settings)XMLSettings.ReadXML(typeof(XMLSettings.Settings), path);

            if (s != null && s.KeysSounds != null && s.KeysSounds.Length > 0)
            {
                var items = new List<ListViewItem>();
                string errors = "";
                string sameKeys = "";

                for (int i = 0; i < s.KeysSounds.Length; i++)
                {
                    int kLength = s.KeysSounds[i].Keys.Length;
                    bool keysNull = (kLength >= 1 && !s.KeysSounds[i].Keys.Any(x => x != 0));
                    int sLength = s.KeysSounds[i].SoundLocations.Length;
                    bool soundsNotEmpty = s.KeysSounds[i].SoundLocations.All(x => !string.IsNullOrWhiteSpace(x));
                    Environment.CurrentDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                    bool filesExist = s.KeysSounds[i].SoundLocations.All(x => File.Exists(x));

                    if (keysNull || sLength < 1 || !soundsNotEmpty || !filesExist) //error in XML file
                    {
                        string tempErr = "";

                        if (!keysNull) tempErr = "one or more keys are null";
                        else if (sLength < 1) tempErr = "no sounds provided";
                        else if (!filesExist) tempErr = "one or more sounds do not exist";

                        errors += "Entry #" + i.ToString() + "has an error: " + tempErr + "\r\n";
                    }

                    string keys = (kLength < 1 ? "" : Helper.keysToString(s.KeysSounds[i].Keys));

                    if (keys != "" && items.Count > 0 && items[items.Count - 1].Text == keys && !sameKeys.Contains(keys))
                    {
                        sameKeys += (sameKeys != "" ? ", " : "") + keys;
                    }

                    var temp = new ListViewItem(keys);
                    temp.SubItems.Add((sLength < 1 ? "" : Helper.soundLocsArrayToString(s.KeysSounds[i].SoundLocations)));

                    items.Add(temp); //add even if there was an error, so that the user can fix within the app
                }


                if (items.Count > 0)
                {
                    if (errors != "")
                    {
                        MessageBox.Show((errors == "" ? "" : errors));
                    }

                    if (sameKeys != "")
                    {
                        MessageBox.Show("Multiple entries using the same keys. The keys being used multiple times are: " + sameKeys);
                    }

                    keysSounds.Clear();
                    keysSounds.AddRange(s.KeysSounds);

                    lvKeySounds.Items.Clear();
                    lvKeySounds.Items.AddRange(items.ToArray());

                    chKeys.Width = -2;
                    chSoundLoc.Width = -2;

                    xmlLoc = path;
                }
                else
                {
                    SystemSounds.Beep.Play();
                    MessageBox.Show("No entries found, or all entries had errors in them (key being None, sound location behind empty or non-existant)");
                }
            }
            else
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("No entries found, or there was an error reading the settings file");
            }
        }

        private void loopbackSourceStream_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (loopbackWaveProvider != null && loopbackWaveProvider.BufferedDuration.TotalMilliseconds <= 100)
                loopbackWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new SettingsForm();
            form.ShowDialog();
        }

        private void texttospeechToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new TextToSpeechForm();
            form.ShowDialog();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            var form = new AddEditHotkeyForm();
            form.ShowDialog();
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            if (lvKeySounds.SelectedItems.Count > 0)
            {
                var form = new AddEditHotkeyForm();

                ListViewItem item = lvKeySounds.SelectedItems[0];

                form.editSoundKeys = new string[2];
                form.editSoundKeys[0] = item.Text;
                form.editSoundKeys[1] = item.SubItems[1].Text;

                form.editIndex = lvKeySounds.SelectedIndices[0];

                form.ShowDialog();
            }
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (lvKeySounds.SelectedItems.Count > 0 && MessageBox.Show("Are you sure remove that item?", "Remove", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                keysSounds.RemoveAt(lvKeySounds.SelectedIndices[0]);
                lvKeySounds.Items.Remove(lvKeySounds.SelectedItems[0]);

                if (lvKeySounds.Items.Count == 0) cbEnable.Checked = false;
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to clear all items?", "Clear", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                keysSounds.Clear();
                lvKeySounds.Items.Clear();

                cbEnable.Checked = false;
            }
        }

        private void btnPlaySound_Click(object sender, EventArgs e)
        {
            if (lvKeySounds.SelectedItems.Count > 0)
                playKeySound(keysSounds[lvKeySounds.SelectedIndices[0]]);
        }

        private void btnStopAllSounds_Click(object sender, EventArgs e)
        {
            stopPlayback();
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            var diag = new OpenFileDialog();

            diag.Filter = "XML file containing keys and sounds|*.xml";

            var result = diag.ShowDialog();

            if (result == DialogResult.OK)
            {
                string path = diag.FileName;

                loadXMLFile(path);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (xmlLoc == "" || !File.Exists(xmlLoc))
                xmlLoc = Helper.userGetXMLLoc();

            if (xmlLoc != "")
            {
                XMLSettings.WriteXML(new XMLSettings.Settings() { KeysSounds = keysSounds.ToArray() }, xmlLoc);

                MessageBox.Show("Saved");
            }
        }

        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            string last = xmlLoc;

            xmlLoc = Helper.userGetXMLLoc();

            if (xmlLoc == "")
                xmlLoc = last;
            else if (last != xmlLoc)
            {
                XMLSettings.WriteXML(new XMLSettings.Settings() { KeysSounds = keysSounds.ToArray() }, xmlLoc);

                MessageBox.Show("Saved");
            }
        }

        private void btnReloadDevices_Click(object sender, EventArgs e)
        {
            stopPlayback();
            stopLoopback();

            loadSoundDevices();
        }

        private void cbEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (cbEnable.Checked)
            {
                //enable timer if there are any keys to check. start loopback
                if ((keysSounds != null && keysSounds.Count > 0) || (XMLSettings.soundboardSettings.LoadXMLFiles != null && XMLSettings.soundboardSettings.LoadXMLFiles.Length > 0))
                    mainTimer.Enabled = true;
                else
                    cbEnable.Checked = false;

                if (cbEnable.Checked && cbPlaybackDevices.Items.Count > 0 && cbLoopbackDevices.SelectedIndex > 0)
                    startLoopback();
            }
            else
            {
                //disable timer, sounds, and loopback
                mainTimer.Enabled = false;

                stopPlayback();
                stopLoopback();
            }
        }

        private void lvKeySounds_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            btnEdit_Click(null, null); //open edit form
        }

        Keys[] keysJustPressed = null;
        bool showingMsgBox = false;
        int lastIndex = -1;

        private void mainTimer_Tick(object sender, EventArgs e)
        {
            if (cbEnable.Checked)
            {
                int keysPressed = 0;

                if (keysSounds.Count > 0) //check that required keys are pressed to play sound
                {
                    for (int i = 0; i < keysSounds.Count; i++)
                    {
                        keysPressed = 0;

                        if (keysSounds[i].Keys.Length == 0) continue;

                        for (int j = 0; j < keysSounds[i].Keys.Length; j++)
                        {
                            if (Keyboard.IsKeyDown(keysSounds[i].Keys[j]))
                                keysPressed++;
                        }

                        if (keysPressed == keysSounds[i].Keys.Length)
                        {
                            if (keysJustPressed == keysSounds[i].Keys) continue;

                            if (keysSounds[i].Keys.Length > 0 && keysSounds[i].Keys.All(x => x != 0) && keysSounds[i].SoundLocations.Length > 0
                                && keysSounds[i].SoundLocations.Length > 0 && keysSounds[i].SoundLocations.Any(x => File.Exists(x)))
                            {
                                if (cbEnablePushToTalk.Checked && !keyUpPushToTalkKey && !Keyboard.IsKeyDown(pushToTalkKey)
                                    && Helper.isForegroundWindow((string)cbWindows.SelectedItem))
                                {
                                    keyUpPushToTalkKey = true;
                                    bool result = Keyboard.sendKey(pushToTalkKey, true);
                                    Thread.Sleep(100);
                                }

                                playKeySound(keysSounds[i]);
                                return;
                            }
                        }
                        else if (keysJustPressed == keysSounds[i].Keys)
                            keysJustPressed = null;
                    }

                    keysPressed = 0;
                }

                if (XMLSettings.soundboardSettings.StopSoundKeys != null && XMLSettings.soundboardSettings.StopSoundKeys.Length > 0) //check that required keys are pressed to stop all sounds
                {
                    for (int i = 0; i < XMLSettings.soundboardSettings.StopSoundKeys.Length; i++)
                    {
                        if (Keyboard.IsKeyDown(XMLSettings.soundboardSettings.StopSoundKeys[i])) keysPressed++;
                    }

                    if (keysPressed == XMLSettings.soundboardSettings.StopSoundKeys.Length)
                    {
                        if (keysJustPressed == null || !keysJustPressed.Intersect(XMLSettings.soundboardSettings.StopSoundKeys).Any())
                        {
                            if (playbackWaveOut != null && playbackWaveOut.PlaybackState == PlaybackState.Playing) playbackWaveOut.Stop();

                            keysJustPressed = XMLSettings.soundboardSettings.StopSoundKeys;

                            return;
                        }
                    }
                    else if (keysJustPressed == XMLSettings.soundboardSettings.StopSoundKeys)
                        keysJustPressed = null;

                    keysPressed = 0;
                }

                if (XMLSettings.soundboardSettings.LoadXMLFiles != null && XMLSettings.soundboardSettings.LoadXMLFiles.Length > 0) //check that required keys are pressed to load XML file
                {
                    for (int i = 0; i < XMLSettings.soundboardSettings.LoadXMLFiles.Length; i++)
                    {
                        if (XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys.Length == 0) continue;

                        keysPressed = 0;

                        for (int j = 0; j < XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys.Length; j++)
                        {
                            if (Keyboard.IsKeyDown(XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys[j])) keysPressed++;
                        }

                        if (keysPressed == XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys.Length)
                        {
                            if (keysJustPressed == null || !keysJustPressed.Intersect(XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys).Any())
                            {
                                if (!string.IsNullOrWhiteSpace(XMLSettings.soundboardSettings.LoadXMLFiles[i].XMLLocation) && File.Exists(XMLSettings.soundboardSettings.LoadXMLFiles[i].XMLLocation))
                                {
                                    keysJustPressed = XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys;

                                    loadXMLFile(XMLSettings.soundboardSettings.LoadXMLFiles[i].XMLLocation);
                                }

                                return;
                            }
                        }
                        else if (keysJustPressed == XMLSettings.soundboardSettings.LoadXMLFiles[i].Keys)
                        {
                            keysJustPressed = null;
                        }
                    }

                    keysPressed = 0;
                }

                if (keyUpPushToTalkKey)
                {
                    if (!Keyboard.IsKeyDown(pushToTalkKey)) keyUpPushToTalkKey = false;

                    if (playbackWaveOut.PlaybackState != PlaybackState.Playing || !Helper.isForegroundWindow((string)cbWindows.SelectedItem))
                    {
                        keyUpPushToTalkKey = false;
                        Keyboard.sendKey(pushToTalkKey, false);
                    }
                }
            }
        }

        private void playKeySound(XMLSettings.KeysSounds currentKeysSounds)
        {
            Environment.CurrentDirectory = Path.GetDirectoryName(Application.ExecutablePath);

            string path;

            if (currentKeysSounds.SoundLocations.Length > 1)
            {
                //get random sound
                int temp;

                while (true)
                {
                    temp = rand.Next(0, currentKeysSounds.SoundLocations.Length);

                    if (temp != lastIndex && File.Exists(currentKeysSounds.SoundLocations[temp])) break;
                    Thread.Sleep(1);
                }

                lastIndex = temp;

                path = currentKeysSounds.SoundLocations[lastIndex];
            }
            else
                path = currentKeysSounds.SoundLocations[0]; //get first sound

            if (File.Exists(path))
            {
                playSound(path);
                keysJustPressed = currentKeysSounds.Keys;
            }
            else if (!showingMsgBox) //dont run when already showing messagebox (don't want a bunch of these on your screen, do you?)
            {
                SystemSounds.Beep.Play();
                showingMsgBox = true;
                MessageBox.Show("File " + path + " does not exist");
                showingMsgBox = false;
            }
        }

        private void cbLoopbackDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbLoopbackDevices.SelectedIndex > 0)
            {
                if (cbEnable.Checked) //start loopback on new device, or stop loopback
                {
                    if ((string)cbLoopbackDevices.SelectedItem == "") stopLoopback();
                    else startLoopback();
                }
                else
                    stopLoopback();
            }

            string deviceName = (string)cbLoopbackDevices.SelectedItem;
            XMLSettings.soundboardSettings.LastLoopbackDevice = deviceName;

            XMLSettings.SaveSoundboardSettingsXML();
        }

        private void cbPlaybackDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            //start loopback on new device and stop all sounds playing
            if (loopbackWaveOut != null && loopbackSourceStream != null && cbEnable.Checked)
                startLoopback();

            stopPlayback();

            string deviceName = (string)cbPlaybackDevices.SelectedItem;
            XMLSettings.soundboardSettings.LastPlaybackDevice = deviceName;

            XMLSettings.SaveSoundboardSettingsXML();
        }

        private void frmMain_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                notifyIcon1.Visible = true;

                this.Hide();
            }
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            notifyIcon1.Visible = false;

            //show form and give focus
            this.WindowState = FormWindowState.Minimized;
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void tbPushToTalkKey_Enter(object sender, EventArgs e)
        {
            if (!cbEnablePushToTalk.Checked)
            {
                cbEnable.Checked = false;
                pushToTalkKeyTimer.Enabled = true;
            }
        }

        private void tbPushToTalkKey_Leave(object sender, EventArgs e)
        {
            pushToTalkKeyTimer.Enabled = false;
        }

        private void pushToTalkKeyTimer_Tick(object sender, EventArgs e)
        {
            if (Keyboard.IsKeyDown(Keys.Escape))
            {
                tbPushToTalkKey.Text = "";
                pushToTalkKey = default(Keys);
            }
            else
            {
                foreach (Keys key in Enum.GetValues(typeof(Keys)))
                {
                    if (Keyboard.IsKeyDown(key))
                    {
                        tbPushToTalkKey.Text = Helper.keysToString(key);
                        pushToTalkKey = key;
                        break;
                    }
                }
            }
        }

        private void cbEnablePushToTalk_CheckedChanged(object sender, EventArgs e)
        {
            if (cbEnablePushToTalk.Checked)
            {
                if (tbPushToTalkKey.Text == "" || (string)cbWindows.SelectedItem == "")
                {
                    cbEnablePushToTalk.Checked = false;
                    MessageBox.Show("There is either no push to talk key entered, or no window selected");
                    return;
                }

                cbWindows.Enabled = false;
            }
            else cbWindows.Enabled = true;
        }

        private void btnReloadWindows_Click(object sender, EventArgs e)
        {
            loadWindows();
        }

        private void cbWindows_SelectedIndexChanged(object sender, EventArgs e)
        {
            cbEnable.Checked = false;
        }

        
    }
}
