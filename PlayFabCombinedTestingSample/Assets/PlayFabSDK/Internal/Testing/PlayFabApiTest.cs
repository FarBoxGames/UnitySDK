﻿using PlayFab.ClientModels;
using PlayFab.Internal;
using PlayFab.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PlayFab.UUnit
{
    /// <summary>
    /// A real system would potentially run only the client or server API, and not both.
    /// But, they still interact with eachother directly.
    /// The tests can't be independent for Client/Server, as the sequence of calls isn't really independent for real-world scenarios.
    /// The client logs in, which triggers a server, and then back and forth.
    /// For the purpose of testing, they each have pieces of information they share with one another, and that sharing makes various calls possible.
    /// </summary>
    public class PlayFabApiTest : UUnitTestCase
    {
        private const int TEST_STAT_BASE = 10;
        private const string TEST_STAT_NAME = "str";
        private const string CHAR_TEST_TYPE = "Test";
        private const string TEST_DATA_KEY = "testCounter";


        // Functional
        private static bool EXEC_ONCE = true;
        private static bool TITLE_INFO_SET = false;
        private static bool TITLE_CAN_UPDATE_SETTINGS = false;

        // Fixed values provided from testInputs
        private static string USER_NAME;
        private static string USER_EMAIL;
        private static string USER_PASSWORD;
        private static string CHAR_NAME;

        // Information fetched by appropriate API calls
        private static string playFabId;
        private static string characterId;

        // This test operates multi-threaded, so keep some thread-transfer varaibles
        private string lastReceivedMessage;
        private ClientModels.UserDataRecord testCounterReturn;
        private int testStatReturn;
        ServerModels.CharacterResult targetCharacter = null;

        /// <summary>
        /// PlayFab Title cannot be created from SDK tests, so you must provide your titleId to run unit tests.
        /// (Also, we don't want lots of excess unused titles)
        /// </summary>
        public static void SetTitleInfo(Dictionary<string, string> testInputs)
        {
            string eachValue;

            PlayFabHTTP.instance.Awake();
            PlayFabSettings.RequestType = WebRequestType.HttpWebRequest;

            TITLE_INFO_SET = true;

            // Parse all the inputs
            TITLE_INFO_SET &= testInputs.TryGetValue("titleId", out eachValue);
            PlayFabSettings.TitleId = eachValue;
            TITLE_INFO_SET &= testInputs.TryGetValue("developerSecretKey", out eachValue);
            PlayFabSettings.DeveloperSecretKey = eachValue;

            TITLE_INFO_SET &= testInputs.TryGetValue("titleCanUpdateSettings", out eachValue);
            TITLE_INFO_SET &= bool.TryParse(eachValue, out TITLE_CAN_UPDATE_SETTINGS);

            TITLE_INFO_SET &= testInputs.TryGetValue("userName", out USER_NAME);
            TITLE_INFO_SET &= testInputs.TryGetValue("userEmail", out USER_EMAIL);
            TITLE_INFO_SET &= testInputs.TryGetValue("userPassword", out USER_PASSWORD);

            TITLE_INFO_SET &= testInputs.TryGetValue("characterName", out CHAR_NAME);

            // Verify all the inputs won't cause crashes in the tests
            TITLE_INFO_SET &= !string.IsNullOrEmpty(PlayFabSettings.TitleId)
                && !string.IsNullOrEmpty(PlayFabSettings.DeveloperSecretKey)
                && !string.IsNullOrEmpty(USER_NAME)
                && !string.IsNullOrEmpty(USER_EMAIL)
                && !string.IsNullOrEmpty(USER_PASSWORD)
                && !string.IsNullOrEmpty(CHAR_NAME);
        }

        protected override void SetUp()
        {
            if (EXEC_ONCE)
            {
                string filename = "C:/depot/pf-main/tools/SDKBuildScripts/testTitleData.json"; // TODO: Figure out how to not hard code this
                if (File.Exists(filename))
                {
                    string testInputsFile = File.ReadAllText(filename);
                    var serializer = JsonSerializer.Create(PlayFab.Internal.Util.JsonSettings);
                    var testInputs = serializer.Deserialize<Dictionary<string, string>>(new JsonTextReader(new StringReader(testInputsFile)));
                    PlayFabApiTest.SetTitleInfo(testInputs);
                }
                else
                {
                    Console.WriteLine("Loading testSettings file failed: " + filename);
                    Console.WriteLine("From: " + Directory.GetCurrentDirectory());
                }
                EXEC_ONCE = false;
            }

            if (!TITLE_INFO_SET)
                UUnitAssert.Skip(); // We cannot do client tests if the titleId is not given
        }

        protected override void TearDown()
        {
            // TODO: Destroy any characters
        }

        private void WaitForApiCalls()
        {
            lastReceivedMessage = null;
            while (PlayFabHTTP.instance.GetPendingMessages() != 0)
                Thread.Sleep(1); // Wait for the threaded call to be executed
            PlayFabHTTP.instance.Update(); // Invoke the callbacks for any threaded messages
            UUnitAssert.NotNull(lastReceivedMessage);
        }

        private void SharedErrorCallback(PlayFabError error)
        {
            lastReceivedMessage = error.ErrorMessage;
        }

        /// <summary>
        /// CLIENT API
        /// Try to deliberately log in with an inappropriate password,
        ///   and verify that the error displays as expected.
        /// </summary>
        [UUnitTest]
        public void InvalidLogin()
        {
            // If the setup failed to log in a user, we need to create one.
            var request = new ClientModels.LoginWithEmailAddressRequest();
            request.TitleId = PlayFabSettings.TitleId;
            request.Email = USER_EMAIL;
            request.Password = USER_PASSWORD + "INVALID";
            PlayFabClientAPI.LoginWithEmailAddress(request, LoginCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.False(lastReceivedMessage.ToLower().Contains("successful"), lastReceivedMessage);
            UUnitAssert.True(lastReceivedMessage.ToLower().Contains("password"), lastReceivedMessage);
        }
        private void LoginCallback(LoginResult result)
        {
            playFabId = result.PlayFabId;
            lastReceivedMessage = "Login Successful";
        }

        /// <summary>
        /// CLIENT API
        /// Log in or create a user, track their PlayFabId
        /// </summary>
        [UUnitTest]
        public void LoginOrRegister()
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) // If we haven't already logged in...
            {
                var loginRequest = new ClientModels.LoginWithEmailAddressRequest();
                loginRequest.Email = USER_EMAIL;
                loginRequest.Password = USER_PASSWORD;
                loginRequest.TitleId = PlayFabSettings.TitleId;
                PlayFabClientAPI.LoginWithEmailAddress(loginRequest, LoginCallback, SharedErrorCallback);
                WaitForApiCalls();

                // We don't do any test here, because the user may not exist, and thus login might fail, but the test should not
            }

            if (PlayFabClientAPI.IsClientLoggedIn())
                return; // Success, already logged in

            // If the setup failed to log in a user, we need to create one.
            var registerRequest = new ClientModels.RegisterPlayFabUserRequest();
            registerRequest.TitleId = PlayFabSettings.TitleId;
            registerRequest.Username = USER_NAME;
            registerRequest.Email = USER_EMAIL;
            registerRequest.Password = USER_PASSWORD;
            PlayFabClientAPI.RegisterPlayFabUser(registerRequest, RegisterCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Registration Successful", lastReceivedMessage); // If we get here, we definitely registered a new user, and we definitely want to verify success

            UUnitAssert.True(PlayFabClientAPI.IsClientLoggedIn(), "User login failed");
        }
        private void RegisterCallback(RegisterPlayFabUserResult result)
        {
            playFabId = result.PlayFabId;
            lastReceivedMessage = "User Registration Successful";
        }

        /// <summary>
        /// CLIENT API
        /// Test a sequence of calls that modifies saved data,
        ///   and verifies that the next sequential API call contains updated data.
        /// Verify that the data is correctly modified on the next call.
        /// Parameter types tested: string, Dictionary<string, string>, DateTime
        /// </summary>
        [UUnitTest]
        public void UserDataApi()
        {
            int testCounterValueExpected, testCounterValueActual;
            DateTime timeInitial, timeUpdated;

            var getRequest = new ClientModels.GetUserDataRequest();
            PlayFabClientAPI.GetUserData(getRequest, GetUserDataCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Data Received", lastReceivedMessage);
            int.TryParse(testCounterReturn.Value, out testCounterValueExpected);
            timeInitial = testCounterReturn.LastUpdated;
            testCounterValueExpected = (testCounterValueExpected + 1) % 100; // This test is about the expected value changing - but not testing more complicated issues like bounds

            var updateRequest = new ClientModels.UpdateUserDataRequest();
            updateRequest.Data = new Dictionary<string, string>();
            updateRequest.Data[TEST_DATA_KEY] = testCounterValueExpected.ToString();
            PlayFabClientAPI.UpdateUserData(updateRequest, UpdateUserDataCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Data Updated", lastReceivedMessage);

            getRequest = new ClientModels.GetUserDataRequest();
            PlayFabClientAPI.GetUserData(getRequest, GetUserDataCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Data Received", lastReceivedMessage);
            int.TryParse(testCounterReturn.Value, out testCounterValueActual);
            timeUpdated = testCounterReturn.LastUpdated;
            UUnitAssert.Equals(testCounterValueExpected, testCounterValueActual);
            UUnitAssert.True(timeUpdated > timeInitial);

            // UnityEngine.Debug.Log((DateTime.UtcNow - timeUpdated).TotalSeconds);
            UUnitAssert.True(Math.Abs((DateTime.UtcNow - timeUpdated).TotalMinutes) < 5); // Make sure that this timestamp is recent - This must also account for the difference between local machine time and server time
        }
        private void GetUserDataCallback(GetUserDataResult result)
        {
            lastReceivedMessage = "User Data Received";

            if (!result.Data.TryGetValue(TEST_DATA_KEY, out testCounterReturn))
            {
                testCounterReturn = new ClientModels.UserDataRecord();
                testCounterReturn.Value = "0";
            }
        }
        private void UpdateUserDataCallback(UpdateUserDataResult result)
        {
            lastReceivedMessage = "User Data Updated";
        }

        /// <summary>
        /// CLIENT API
        /// Test a sequence of calls that modifies saved data,
        ///   and verifies that the next sequential API call contains updated data.
        /// Verify that the data is saved correctly, and that specific types are tested
        /// Parameter types tested: Dictionary<string, int> 
        /// </summary>
        [UUnitTest]
        public void UserStatisticsApi()
        {
            int testStatExpected, testStatActual;

            var getRequest = new ClientModels.GetUserStatisticsRequest();
            PlayFabClientAPI.GetUserStatistics(getRequest, GetUserStatsCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Stats Received", lastReceivedMessage);
            testStatExpected = ((testStatReturn + 1) % TEST_STAT_BASE) + TEST_STAT_BASE; // This test is about the expected value changing (incrementing through from TEST_STAT_BASE to TEST_STAT_BASE * 2 - 1)

            var updateRequest = new ClientModels.UpdateUserStatisticsRequest();
            updateRequest.UserStatistics = new Dictionary<string, int>();
            updateRequest.UserStatistics[TEST_STAT_NAME] = testStatExpected;
            PlayFabClientAPI.UpdateUserStatistics(updateRequest, UpdateUserStatsCallback, SharedErrorCallback);
            WaitForApiCalls();

            // Test update result - no data returned, so error or no error, based on Title settings
            if (!TITLE_CAN_UPDATE_SETTINGS)
            {
                UUnitAssert.Equals("error message from PlayFab", lastReceivedMessage);
                return; // The rest of this tests changing settings - Which we verified we cannot do
            }
            else // if (CAN_UPDATE_SETTINGS)
            {
                UUnitAssert.Equals("User Stats Updated", lastReceivedMessage);
            }

            getRequest = new ClientModels.GetUserStatisticsRequest();
            PlayFabClientAPI.GetUserStatistics(getRequest, GetUserStatsCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("User Stats Received", lastReceivedMessage);
            testStatActual = testStatReturn;
            UUnitAssert.Equals(testStatExpected, testStatActual);
        }
        private void GetUserStatsCallback(GetUserStatisticsResult result)
        {
            lastReceivedMessage = "User Stats Received";

            if (!result.UserStatistics.TryGetValue(TEST_STAT_NAME, out testStatReturn))
                testStatReturn = TEST_STAT_BASE;
        }
        private void UpdateUserStatsCallback(UpdateUserStatisticsResult result)
        {
            lastReceivedMessage = "User Stats Updated";
        }

        /// <summary>
        /// SERVER API
        /// Get or create the given test character for the given user
        /// Parameter types tested: Contained-Classes, string
        /// </summary>
        [UUnitTest]
        public void UserCharacter()
        {
            var request = new ServerModels.ListUsersCharactersRequest();
            request.PlayFabId = playFabId; // Received from client upon login
            PlayFabServerAPI.GetAllUsersCharacters(request, GetCharsCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("Get Chars Successful", lastReceivedMessage);
            // The target character may not exist, but we don't fail, since we can create it below

            if (targetCharacter == null)
            {
                // Create the targetCharacter since it doesn't exist
                var grantRequest = new ServerModels.GrantCharacterToUserRequest();
                grantRequest.PlayFabId = playFabId;
                grantRequest.CharacterName = CHAR_NAME;
                grantRequest.CharacterType = CHAR_TEST_TYPE;
                PlayFabServerAPI.GrantCharacterToUser(grantRequest, GrantCharCallback, SharedErrorCallback);
                WaitForApiCalls();

                UUnitAssert.Equals("Grant Char Successful", lastReceivedMessage);

                // Attempt to get characters again
                PlayFabServerAPI.GetAllUsersCharacters(request, GetCharsCallback, SharedErrorCallback);
                WaitForApiCalls();

                UUnitAssert.Equals("Get Chars Successful", lastReceivedMessage);
            }

            // Save the requested character
            UUnitAssert.NotNull(targetCharacter, "The test character did not exist, and was not successfully created");
            characterId = targetCharacter.CharacterId;
        }
        private void GetCharsCallback(PlayFab.ServerModels.ListUsersCharactersResult result)
        {
            lastReceivedMessage = "Get Chars Successful";
            foreach (var eachCharacter in result.Characters)
                if (eachCharacter.CharacterName == CHAR_NAME)
                    targetCharacter = eachCharacter;
        }
        private void GrantCharCallback(PlayFab.ServerModels.GrantCharacterToUserResult result)
        {
            lastReceivedMessage = "Grant Char Successful";
        }

        /// <summary>
        /// CLIENT AND SERVER API
        /// Test that leaderboard results can be requested
        /// Parameter types tested: List of contained-classes
        /// </summary>
        [UUnitTest]
        public void LeaderBoard()
        {
            var clientRequest = new ClientModels.GetLeaderboardAroundCurrentUserRequest();
            clientRequest.MaxResultsCount = 3;
            clientRequest.StatisticName = TEST_STAT_NAME;
            PlayFabClientAPI.GetLeaderboardAroundCurrentUser(clientRequest, GetClientLbCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("Get Client Leaderboard Successful", lastReceivedMessage);
            // Testing anything more would be testing actual functionality of the Leaderboard, which is outside the scope of this test.

            var serverRequest = new ServerModels.GetLeaderboardAroundCharacterRequest();
            serverRequest.MaxResultsCount = 3;
            serverRequest.StatisticName = TEST_STAT_NAME;
            serverRequest.CharacterId = characterId;
            serverRequest.PlayFabId = playFabId;
            PlayFabServerAPI.GetLeaderboardAroundCharacter(serverRequest, GetServerLbCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("Get Server Leaderboard Successful", lastReceivedMessage);
        }
        public void GetClientLbCallback(PlayFab.ClientModels.GetLeaderboardAroundCurrentUserResult result)
        {
            if (result.Leaderboard.Count > 0)
                lastReceivedMessage = "Get Client Leaderboard Successful";
            else
                lastReceivedMessage = "Get Client Leaderboard, empty";
        }
        public void GetServerLbCallback(PlayFab.ServerModels.GetLeaderboardAroundCharacterResult result)
        {
            if (result.Leaderboard.Count > 0)
                lastReceivedMessage = "Get Server Leaderboard Successful";
            else
                lastReceivedMessage = "Get Server Leaderboard, empty";
        }

        [UUnitTest]
        public void AccountInfo()
        {
			GetAccountInfoRequest request = new GetAccountInfoRequest();
			request.PlayFabId = playFabId;
            PlayFabClientAPI.GetAccountInfo(request, AcctInfoCallback, SharedErrorCallback);
            WaitForApiCalls();

            UUnitAssert.Equals("Enums tested", lastReceivedMessage);
        }
		private void AcctInfoCallback(GetAccountInfoResult result)
		{
            if (result.AccountInfo == null || result.AccountInfo.TitleInfo == null || result.AccountInfo.TitleInfo.Origination == null
            || !Enum.IsDefined(typeof(UserOrigination), result.AccountInfo.TitleInfo.Origination.Value))
			{
                lastReceivedMessage = "Enums not properly tested";
				return;
			}

            lastReceivedMessage = "Enums tested";
		}
    }
}