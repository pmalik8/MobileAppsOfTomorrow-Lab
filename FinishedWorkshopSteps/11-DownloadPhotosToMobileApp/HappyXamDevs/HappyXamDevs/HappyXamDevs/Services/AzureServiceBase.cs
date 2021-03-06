using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HappyXamDevs.Models;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.WindowsAzure.MobileServices;
using Newtonsoft.Json.Linq;
using Plugin.Media.Abstractions;
using Xamarin.Forms;

namespace HappyXamDevs.Services
{
    public abstract class AzureServiceBase : IAzureService
    {
#error REPLACE [YOUR AZURE APP NAME HERE]
        protected const string AzureAppName = "[YOUR AZURE APP NAME HERE]";
        protected readonly static string FunctionAppUrl = $"https://{AzureAppName}.azurewebsites.net";

        private const string AuthTokenKey = "auth-token";
        private const string PhotoResource = "photo";
        private const string UserIdKey = "user-id";
#error REPLACE [YOUR API KEY HERE]
#error REPLACE [YOUR FACE API BASE URL]
        private readonly FaceClient faceApiClient = new FaceClient(new ApiKeyServiceClientCredentials("[YOUR API KEY HERE]"))
        {
            Endpoint = "[YOUR FACE API BASE URL]"
            //Example Face API Base Url: "https://westus.api.cognitive.microsoft.com/"
        };

        protected AzureServiceBase()
        {
            Client = new MobileServiceClient(FunctionAppUrl);
        }

        public MobileServiceClient Client { get; }

        public async Task<bool> Authenticate()
        {
            if (IsLoggedIn())
                return true;

            try
            {
                await AuthenticateUser();
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }

            if (Client.CurrentUser != null)
            {
                Application.Current.Properties[AuthTokenKey] = Client.CurrentUser.MobileServiceAuthenticationToken;
                Application.Current.Properties[UserIdKey] = Client.CurrentUser.UserId;
                await Application.Current.SavePropertiesAsync();
            }

            return IsLoggedIn();
        }

        public async Task DownloadPhoto(PhotoMetadataModel photoMetadata)
        {
            if (File.Exists(photoMetadata.FileName))
                return;

            var response = await Client.InvokeApiAsync($"photo/{photoMetadata.Name}",
                                                       HttpMethod.Get,
                                                       new Dictionary<string, string>());

            var photo = response["photo"].Value<string>();
            var bytes = Convert.FromBase64String(photo);

            using (var fs = new FileStream(photoMetadata.FileName, FileMode.CreateNew))
                await fs.WriteAsync(bytes, 0, bytes.Length);
        }

        public async Task<IEnumerable<PhotoMetadataModel>> GetAllPhotoMetadata()
        {
            var allMetadata = await Client.InvokeApiAsync<List<PhotoMetadataModel>>(PhotoResource,
                                                                               HttpMethod.Get,
                                                                               new Dictionary<string, string>());

            foreach (var metadata in allMetadata)
                await DownloadPhoto(metadata);

            return allMetadata;
        }

        public bool IsLoggedIn()
        {
            TryLoadUserDetails();
            return Client.CurrentUser != null;
        }

        public async Task UploadPhoto(MediaFile photo)
        {
            using (var photoStream = photo.GetStream())
            {
                var bytes = new byte[photoStream.Length];
                await photoStream.ReadAsync(bytes, 0, Convert.ToInt32(photoStream.Length));

                var content = new
                {
                    Photo = Convert.ToBase64String(bytes)
                };

                var json = JToken.FromObject(content);

                await Client.InvokeApiAsync(PhotoResource, json);
            }
        }

        public async Task<bool> VerifyHappyFace(MediaFile photo)
        {
            using (var photoStream = photo.GetStream())
            {
                var faceAttributes = new List<FaceAttributeType> { FaceAttributeType.Emotion };

                var faces = await faceApiClient.Face.DetectWithStreamAsync(photoStream, returnFaceAttributes: faceAttributes);

                var areHappyFacesDetected = faces.Any(f => f.FaceAttributes.Emotion.Happiness > 0.75);
                return areHappyFacesDetected;
            }
        }

        protected abstract Task AuthenticateUser();

        private void TryLoadUserDetails()
        {
            if (Client.CurrentUser != null) return;

            if (Application.Current.Properties.TryGetValue(AuthTokenKey, out var authToken) &&
                Application.Current.Properties.TryGetValue(UserIdKey, out var userId))
            {
                Client.CurrentUser = new MobileServiceUser(userId.ToString())
                {
                    MobileServiceAuthenticationToken = authToken.ToString()
                };
            }
        }
    }
}