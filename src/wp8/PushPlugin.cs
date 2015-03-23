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
            Debug.WriteLine("PushPlugin.register");
            if (!TryDeserializeOptions(options, out this.pushOptions))
            {
                Debug.WriteLine("PushPlugin.register: deserialize error.");
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            Debug.WriteLine("PushPlugin.register: create push notification channel.");

            IAsyncOperation<PushNotificationChannel> channelTask = null;
            if (this.pushOptions.AppName == null)
            {
                Debug.WriteLine("PushPlugin.register: No app name specified.");
                channelTask = Windows.Networking.PushNotifications.PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();
            }
            else
            {
                Debug.WriteLine("PushPlugin.register: app name set to:" + this.pushOptions.AppName);
                channelTask = Windows.Networking.PushNotifications.PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync(this.pushOptions.AppName);
            }
            Debug.WriteLine("PushPlugin.register: task created");
            pushNotificationChannel = channelTask.AsTask().Result;
            Debug.WriteLine("PushPlugin.register: push notification channel uri: " + pushNotificationChannel.Uri);

            pushNotificationChannel.PushNotificationReceived += OnPushNotificationReceived;
            Debug.WriteLine("PushPlugin.register: event handler added");

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
            Debug.WriteLine("PushPlugin.OnPushNotificationReceived: " + e.NotificationType);

            var pushNotification = new PushNotification();
            switch (e.NotificationType)
            {
                case PushNotificationType.Badge:
                    Debug.WriteLine("PushPlugin.OnPushNotificationReceived Badge notification");
                    pushNotification.Type = "badge";
                    pushNotification.JsonContent.Add("innerText", e.BadgeNotification.Content.InnerText);
                    break;
                case PushNotificationType.Tile:
                    Debug.WriteLine("PushPlugin.OnPushNotificationReceived Tile notification");
                    pushNotification.Type = "tile";
                    pushNotification.JsonContent.Add("innerText", e.TileNotification.Content.InnerText);
                    break;
                case PushNotificationType.Toast:
                    Debug.WriteLine("PushPlugin.OnPushNotificationReceived Toast notification");
                    pushNotification.Type = "toast";
                    pushNotification.JsonContent.Add("innerText", e.ToastNotification.Content.InnerText);
                    break;
                default:
                    // Do nothing
                    Debug.WriteLine("PushPlugin.OnPushNotificationReceived Unknown notification type");
                    return;
            }
            Debug.WriteLine("PushPlugin.OnPushNotificationReceived type set");

            if (this.pushOptions.NotificationCallback != null)
            {
                Debug.WriteLine("PushPlugin.OnPushNotificationReceived - executing callback");
                this.ExecuteCallback(this.pushOptions.NotificationCallback, JsonConvert.SerializeObject(pushNotification));
                Debug.WriteLine("PushPlugin.OnPushNotificationReceived - callback done");

                // prevent the notification from being delivered to the UI, expecting the application to show it
                e.Cancel = true;
                Debug.WriteLine("PushPlugin.OnPushNotificationReceived - event canceled");
            }
            else
            {
                TryExecuteErrorCallback("PushPlugin.OnPushNotificationReceived: No push event callback defined");
            }

            Debug.WriteLine("PushPlugin.OnPushNotificationReceived - done");
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
