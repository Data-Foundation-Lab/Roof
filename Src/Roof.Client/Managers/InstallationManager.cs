using Roof.Common.Networking;
using Roof.Common.Tools;
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Diagnostics;
using Roof.Common.Networking.RNMS;

namespace Roof.Client.Managers
{
    /// <summary>
    /// Manager class for installation.
    /// </summary>
    public class InstallationManager
    {
        private static string? _clientID;

        public static string GenerateUniqueRoofClientID() => Guid.NewGuid().ToString().Replace("-", "");

        public static async Task<RoofNetworkNativeResponsePacket> RegisterClientToRoof(RoofNetworkNativeRequestPacket requestPacket)
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

            //Check if the Client ID or the Roof IP is null or an empty string
            if (string.IsNullOrEmpty(requestPacket.ClientID) || string.IsNullOrEmpty(requestPacket.RoofIP))
            {
                respPacket.Status = false;

                if (string.IsNullOrEmpty(requestPacket.ClientID))
                    respPacket.AddResponse("Invalid Client ID.");
                
                if(string.IsNullOrEmpty(requestPacket.RoofIP))
                    respPacket.AddResponse("Invalid Roof IP.");

                return respPacket;
            }

            //Check if the Client ID is a 128bit hash value
            if (!Security.Is128BitHash(requestPacket.ClientID))
            {
                respPacket.Status = false;
                respPacket.AddResponse("Invalid Client ID format.");

                return respPacket;
            }

            if (EntryPoint.LocalNetworkMessageProvider == null)
            {
                respPacket.Status = false;
                respPacket.AddResponse("Failed to send registration network packet. Local NMP is not initialized.");

                return respPacket;
            }

            EntryPoint.LocalNetworkMessageProvider.OnMessageReceived -= EntryPoint.ClientNetworkMessageProvider_OnMessageReceived;
            EntryPoint.LocalNetworkMessageProvider.OnMessageReceived += ClientRegistrationNetworkMessageProvider_OnMessageReceived;

            _clientID = requestPacket.ClientID;

            requestPacket.Requests = new List<string>() { "TCP INFO REQUEST" };

            string serialisedPacket = JsonSerializer.Serialize(requestPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);
            byte[] packetBuffer = Encoding.UTF8.GetBytes(serialisedPacket);

            bool networkPacketSendResult = await EntryPoint.LocalNetworkMessageProvider.SendMessage(packetBuffer);

            if(networkPacketSendResult)
            {
                respPacket.Status = true;

                return respPacket;
            }
            else
            {
                respPacket.Status = false;
                respPacket.AddResponse("Failed to send registration network packet.");

                return respPacket;
            }
        }

        private static async void ClientRegistrationNetworkMessageProvider_OnMessageReceived(IPEndPoint? clientAddress, string message)
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();
            RoofNetworkNativeResponsePacket? packet;

            if(IsStringJson(message))
            {
                try
                {
                    //Attempt deserialisation.
                    packet = JsonSerializer.Deserialize(message, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
                }
                catch (Exception ex)
                {
                    respPacket.Status = false;
                    respPacket.AddResponse("Failed to get TCP information.");

                    EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                    return;
                }

                if (packet != null && packet.Responses.First() != null && packet.Responses.First().Equals("TCP_ENCRYPTION_INFO"))
                {
                    await HandleTCPEncryptionInfo(packet);
                    return;
                }
            }
            else
            {
                string decryptedMessage;
                try
                {
                    byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
                    decryptedMessage = Security.AES256.ManagedNetworkDecrypt(
                        EntryPoint._encryptionKey ?? string.Empty,
                        EntryPoint. _encryptionIV ?? string.Empty,
                        messageBuffer,
                        messageBuffer.Length);
                }
                catch (Exception ex)
                {
                    EntryPoint.DisposeEncryptionInfo();
                    
                    respPacket.Status = false;
                    respPacket.AddResponse($"Error decrypting Roof response packet.");
                    respPacket.AddResponse(ex.Message);

                    EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                    return;
                }

                RoofNetworkNativeResponsePacket? decryptedPacket;
                try
                {
                    decryptedPacket = JsonSerializer.Deserialize(decryptedMessage, RoofNetworkNativeResponsePacketJSONContext.Default.RoofNetworkNativeResponsePacket);
                }
                catch(Exception ex)
                {
                    EntryPoint.DisposeEncryptionInfo();
                    
                    respPacket.Status = false;
                    respPacket.AddResponse($"Error decrypting Roof response packet.");
                    respPacket.AddResponse(ex.Message);

                    EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                    return;
                }

                string registrationResult = decryptedPacket.Responses.First();

                if (registrationResult.Equals($"RFCLIENT {_clientID} REGISTER SUCCESS"))
                {
                    respPacket.Status = true;
                    respPacket.AddResponse(EntryPoint._encryptionKey ?? string.Empty);
                    respPacket.AddResponse(EntryPoint._encryptionIV ?? string.Empty);

                    EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);
                }
                else
                {
                    EntryPoint.DisposeEncryptionInfo();
                    
                    respPacket.Status = false;
                    respPacket.AddResponse("Failed to register client to the Roof.");

                    EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                    return;
                }

                //Deregister.
                _clientID = "";

                if (EntryPoint.LocalNetworkMessageProvider != null)
                {
                    //Re-route event handler
                    EntryPoint.LocalNetworkMessageProvider.OnMessageReceived -= ClientRegistrationNetworkMessageProvider_OnMessageReceived;
                    EntryPoint.LocalNetworkMessageProvider.OnMessageReceived += EntryPoint.ClientNetworkMessageProvider_OnMessageReceived;
                }
            }
        }

        private static async Task HandleTCPEncryptionInfo(RoofNetworkNativeResponsePacket packet)
        {
            RoofNetworkNativeResponsePacket respPacket = new RoofNetworkNativeResponsePacket();

            string clientID = packet.Responses[1];
            string encryptionKeyBase64 = packet.Responses[2]; //Raw string, since we don't have encryption going on yet at this point.
            string encryptionIVBase64 = packet.Responses[3]; //Raw string, since we don't have encryption going on yet at this point.

            //Check if the encryption key is a valid base64 AES256 key format.
            if (!Security.AES256.IsValidBase64AES256Key(encryptionKeyBase64))
            {
                respPacket.Status = false;
                respPacket.AddResponse("Received invalid TCP encryption key from the Roof.");

                EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                return;
            }

            //Check if the encryption IV is a valid base64 AES IV format.
            if (!Security.AES256.IsValidBase64AESIV(encryptionIVBase64))
            {
                respPacket.Status = false;
                respPacket.AddResponse("Received invalid TCP encryption IV from the Roof.");

                EntryPoint.InvokeRoofClientRegistrationCallbackSubscribers(respPacket);

                return;
            }

            //Set encryption info as soon as we get them.
            EntryPoint.SetEncryptionInfo(encryptionKeyBase64, encryptionIVBase64);

            await RequestClientRegistration(clientID);
        }

        private static async Task RequestClientRegistration(string clientID)
        {
            var requestPacket = new RoofNetworkNativeRequestPacket()
            {
                ClientID = clientID,
                Requests = new List<string>() { "REGISTER REQUEST" }
            };

            var serializedPacket = JsonSerializer.Serialize(requestPacket, RoofNetworkNativeRequestPacketJSONContext.Default.RoofNetworkNativeRequestPacket);

            byte[] encryptedRegReqPacket = Security.AES256.ManagedNetworkEncrypt(
                EntryPoint._encryptionKey ?? string.Empty,
                EntryPoint._encryptionIV ?? string.Empty,
                serializedPacket);

            await EntryPoint.LocalNetworkMessageProvider.SendMessage(encryptedRegReqPacket);
        }

        private static bool IsStringJson(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            try
            {
                using var jsonDoc = JsonDocument.Parse(source);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}