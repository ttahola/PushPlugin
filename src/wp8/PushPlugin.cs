using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Windows;
using Microsoft.Phone.Controls;
using Newtonsoft.Json;
using Windows.Networking.PushNotifications;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Notifications;

namespace WPCordovaClassLib.Cordova.Commands
{
    public class PushPlugin : BaseCommand
    {
        private Options pushOptions;
        private PushNotificationChannel pushNotificationChannel;

        public void register(string options)
        {
            if (!TryDeserializeOptions(options, out this.pushOptions))
            {
                Debug.WriteLine("PushPlugin.register: deserialize error.");
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            IAsyncOperation<PushNotificationChannel> channelTask = null;
            if (this.pushOptions.AppName == null)
            {
                channelTask = Windows.Networking.PushNotifications.PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();
            }
            else
            {
                channelTask = Windows.Networking.PushNotifications.PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync(this.pushOptions.AppName);
            }
            pushNotificationChannel = channelTask.AsTask().Result;
            pushNotificationChannel.PushNotificationReceived += OnPushNotificationReceived;

            this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, pushNotificationChannel.Uri));
        }

        public void unregister(string options)
        {
            try
            {
                pushNotificationChannel.Close();
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, "Channel closed!"));
            }
            catch (Exception ex)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, ex.Message));
            }
        }

        void OnPushNotificationReceived(PushNotificationChannel sender, PushNotificationReceivedEventArgs e)
        {
            var pushNotification = new PushNotification();
            switch (e.NotificationType)
            {
                case PushNotificationType.Badge:
                    pushNotification.Type = "badge";
                    pushNotification.JsonContent.Add("innerText", e.BadgeNotification.Content.InnerText);
                    break;
                case PushNotificationType.Tile:
                    pushNotification.Type = "tile";
                    pushNotification.JsonContent.Add("innerText", e.TileNotification.Content.InnerText);
                    break;
                case PushNotificationType.Toast:
                    pushNotification.Type = "toast";
                    pushNotification.JsonContent.Add("innerText", e.ToastNotification.Content.InnerText);
                    break;
                default:
                    // Do nothing
                    return;
            }

            if (this.pushOptions.NotificationCallback != null)
            {
                this.ExecuteCallback(this.pushOptions.NotificationCallback, JsonConvert.SerializeObject(pushNotification));

                // prevent the notification from being delivered to the UI, expecting the application to show it
                e.Cancel = true;
            }
            else
            {
                TryExecuteErrorCallback("PushPlugin.OnPushNotificationReceived: No push event callback defined");
            }
        }

        void ExecuteCallback(string callback, string callbackResult, bool escalateError = true)
        {
            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                PhoneApplicationFrame frame;
                PhoneApplicationPage page;
                CordovaView cView;

                if (TryCast(Application.Current.RootVisual, out frame) &&
                    TryCast(frame.Content, out page) &&
                    TryCast(page.FindName("CordovaView"), out cView))
                {
                    cView.Browser.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            cView.Browser.InvokeScript("eval", new string[] { callback + "(" + callbackResult + ")" });
                        }
                        catch (Exception ex)
                        {
                            if (escalateError)
                            {
                                TryExecuteErrorCallback("Exception in InvokeScriptCallback :: " + ex.Message);
                            }
                            else
                            {
                                Debug.WriteLine("ERROR: Exception in InvokeScriptCallback :: " + ex.Message);
                            }
                        }
                    });
                }
            });
        }

        void TryExecuteErrorCallback(string message)
        {
            Debug.WriteLine("ERROR: TryExecuteErrorCallback :: " + message);
            try
            {
                if (this.pushOptions.ErrorCallback != null)
                {
                    ExecuteCallback(this.pushOptions.ErrorCallback, JsonConvert.SerializeObject(new PushNotificationError(message)), false);
                }
            }
            catch (Exception ex)
            {
                // Do nothing
                Debug.WriteLine("ERROR: Exception in ExecuteErrorCallback :: " + ex.Message);
            }
        }

        static bool TryDeserializeOptions<T>(string options, out T result) where T : class
        {
            result = null;
            try
            {
                var args = JsonConvert.DeserializeObject<string[]>(options);
                result = JsonConvert.DeserializeObject<T>(args[0]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryCast<T>(object obj, out T result) where T : class
        {
            result = obj as T;
            return result != null;
        }

        [DataContract]
        public class Options
        {
            [DataMember(Name = "appName", IsRequired = false)]
            public string AppName { get; set; }

            [DataMember(Name = "ecb", IsRequired = false)]
            public string NotificationCallback { get; set; }

            [DataMember(Name = "errcb", IsRequired = false)]
            public string ErrorCallback { get; set; }

            [DataMember(Name = "uccb", IsRequired = false)]
            public string UriChangedCallback { get; set; }
        }

        [DataContract]
        public class PushNotification
        {
            public PushNotification()
            {
                this.JsonContent = new Dictionary<string, object>();
            }

            [DataMember(Name = "jsonContent", IsRequired = true)]
            public IDictionary<string, object> JsonContent { get; set; }

            [DataMember(Name = "type", IsRequired = true)]
            public string Type { get; set; }
        }

        [DataContract]
        public class PushNotificationError
        {
            public PushNotificationError(string message)
            {
                this.Message = message;
            }
            [DataMember(Name = "message", IsRequired = true)]
            public string Message { get; set; }
        }
    }
}
