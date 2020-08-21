﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Core.Configuration;
using Umbraco.Core.Logging;
using Umbraco.Core.Mapping;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Web.BackOffice.Filters;
using Umbraco.Web.Common.Attributes;
using Umbraco.Web.Common.Exceptions;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Web.Security;
using Constants = Umbraco.Core.Constants;
using Umbraco.Core.Configuration.Models;
using Microsoft.Extensions.Options;

namespace Umbraco.Web.BackOffice.Controllers
{
    /// <inheritdoc />
    /// <summary>
    /// The API controller used for editing dictionary items
    /// </summary>
    /// <remarks>
    /// The security for this controller is defined to allow full CRUD access to dictionary if the user has access to either:
    /// Dictionary
    /// </remarks>
    [PluginController(Constants.Web.Mvc.BackOfficeApiArea)]
    [UmbracoTreeAuthorize(Constants.Trees.Dictionary)]
    public class DictionaryController : BackOfficeNotificationsController
    {
        private readonly ILogger _logger;
        private readonly ILocalizationService _localizationService;
        private readonly IWebSecurity _webSecurity;
        private readonly GlobalSettings _globalSettings;
        private readonly ILocalizedTextService _localizedTextService;
        private readonly UmbracoMapper _umbracoMapper;

        public DictionaryController(
            ILogger logger,
            ILocalizationService localizationService,
            IWebSecurity webSecurity,
            IOptionsSnapshot<GlobalSettings> globalSettings,
            ILocalizedTextService localizedTextService,
            UmbracoMapper umbracoMapper
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
            _webSecurity = webSecurity ?? throw new ArgumentNullException(nameof(webSecurity));
            _globalSettings = globalSettings.Value ?? throw new ArgumentNullException(nameof(globalSettings));
            _localizedTextService = localizedTextService ?? throw new ArgumentNullException(nameof(localizedTextService));
            _umbracoMapper = umbracoMapper ?? throw new ArgumentNullException(nameof(umbracoMapper));
        }

        /// <summary>
        /// Deletes a data type with a given ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns><see cref="HttpResponseMessage"/></returns>
        [HttpDelete]
        [HttpPost]
        public IActionResult DeleteById(int id)
        {
            var foundDictionary = _localizationService.GetDictionaryItemById(id);

            if (foundDictionary == null)
                return NotFound();

            var foundDictionaryDescendants = _localizationService.GetDictionaryItemDescendants(foundDictionary.Key);

            foreach (var dictionaryItem in foundDictionaryDescendants)
            {
                _localizationService.Delete(dictionaryItem, _webSecurity.CurrentUser.Id);
            }

            _localizationService.Delete(foundDictionary, _webSecurity.CurrentUser.Id);

            return Ok();
        }

        /// <summary>
        /// Creates a new dictionary item
        /// </summary>
        /// <param name="parentId">
        /// The parent id.
        /// </param>
        /// <param name="key">
        /// The key.
        /// </param>
        /// <returns>
        /// The <see cref="HttpResponseMessage"/>.
        /// </returns>
        [HttpPost]
        public ActionResult<int> Create(int parentId, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw HttpResponseException.CreateNotificationValidationErrorResponse("Key can not be empty."); // TODO: translate

            if (_localizationService.DictionaryItemExists(key))
            {
                var message = _localizedTextService.Localize(
                     "dictionaryItem/changeKeyError",
                     _webSecurity.CurrentUser.GetUserCulture(_localizedTextService, _globalSettings),
                     new Dictionary<string, string> { { "0", key } });
                throw HttpResponseException.CreateNotificationValidationErrorResponse(message);
            }

            try
            {
                Guid? parentGuid = null;

                if (parentId > 0)
                    parentGuid = _localizationService.GetDictionaryItemById(parentId).Key;

                var item = _localizationService.CreateDictionaryItemWithIdentity(
                    key,
                    parentGuid,
                    string.Empty);


                return item.Id;
            }
            catch (Exception ex)
            {
                _logger.Error(GetType(), ex, "Error creating dictionary with {Name} under {ParentId}", key, parentId);
                throw HttpResponseException.CreateNotificationValidationErrorResponse("Error creating dictionary item");
            }
        }

        /// <summary>
        /// Gets a dictionary item by id
        /// </summary>
        /// <param name="id">
        /// The id.
        /// </param>
        /// <returns>
        /// The <see cref="DictionaryDisplay"/>.
        /// </returns>
        /// <exception cref="HttpResponseException">
        ///  Returns a not found response when dictionary item does not exist
        /// </exception>
        public ActionResult<DictionaryDisplay> GetById(int id)
        {
            var dictionary = _localizationService.GetDictionaryItemById(id);

            if (dictionary == null)
                return NotFound();

            return _umbracoMapper.Map<IDictionaryItem, DictionaryDisplay>(dictionary);
        }

        /// <summary>
        /// Saves a dictionary item
        /// </summary>
        /// <param name="dictionary">
        /// The dictionary.
        /// </param>
        /// <returns>
        /// The <see cref="DictionaryDisplay"/>.
        /// </returns>
        public DictionaryDisplay PostSave(DictionarySave dictionary)
        {
            var dictionaryItem =
                _localizationService.GetDictionaryItemById(int.Parse(dictionary.Id.ToString()));

            if (dictionaryItem == null)
                throw HttpResponseException.CreateNotificationValidationErrorResponse("Dictionary item does not exist");

            var userCulture = _webSecurity.CurrentUser.GetUserCulture(_localizedTextService, _globalSettings);

            if (dictionary.NameIsDirty)
            {
                // if the name (key) has changed, we need to check if the new key does not exist
                var dictionaryByKey = _localizationService.GetDictionaryItemByKey(dictionary.Name);

                if (dictionaryByKey != null && dictionaryItem.Id != dictionaryByKey.Id)
                {

                    var message = _localizedTextService.Localize(
                        "dictionaryItem/changeKeyError",
                        userCulture,
                        new Dictionary<string, string> { { "0", dictionary.Name } });
                    ModelState.AddModelError("Name", message);
                    throw HttpResponseException.CreateValidationErrorResponse(ModelState);
                }

                dictionaryItem.ItemKey = dictionary.Name;
            }

            foreach (var translation in dictionary.Translations)
            {
                _localizationService.AddOrUpdateDictionaryValue(dictionaryItem,
                    _localizationService.GetLanguageById(translation.LanguageId), translation.Translation);
            }

            try
            {
                _localizationService.Save(dictionaryItem);

                var model = _umbracoMapper.Map<IDictionaryItem, DictionaryDisplay>(dictionaryItem);

                model.Notifications.Add(new BackOfficeNotification(
                    _localizedTextService.Localize("speechBubbles/dictionaryItemSaved", userCulture), string.Empty,
                    NotificationStyle.Success));

                return model;
            }
            catch (Exception ex)
            {
                _logger.Error(GetType(), ex, "Error saving dictionary with {Name} under {ParentId}", dictionary.Name, dictionary.ParentId);
                throw HttpResponseException.CreateNotificationValidationErrorResponse("Something went wrong saving dictionary");
            }
        }

        /// <summary>
        /// Retrieves a list with all dictionary items
        /// </summary>
        /// <returns>
        /// The <see cref="IEnumerable{T}"/>.
        /// </returns>
        public IEnumerable<DictionaryOverviewDisplay> GetList()
        {
            var list = new List<DictionaryOverviewDisplay>();

            const int level = 0;

            foreach (var dictionaryItem in _localizationService.GetRootDictionaryItems().OrderBy(ItemSort()))
            {
                var item = _umbracoMapper.Map<IDictionaryItem, DictionaryOverviewDisplay>(dictionaryItem);
                item.Level = 0;
                list.Add(item);

                GetChildItemsForList(dictionaryItem, level + 1, list);
            }

            return list;
        }

        /// <summary>
        /// Get child items for list.
        /// </summary>
        /// <param name="dictionaryItem">
        /// The dictionary item.
        /// </param>
        /// <param name="level">
        /// The level.
        /// </param>
        /// <param name="list">
        /// The list.
        /// </param>
        private void GetChildItemsForList(IDictionaryItem dictionaryItem, int level, ICollection<DictionaryOverviewDisplay> list)
        {
            foreach (var childItem in _localizationService.GetDictionaryItemChildren(dictionaryItem.Key).OrderBy(ItemSort()))
            {
                var item = _umbracoMapper.Map<IDictionaryItem, DictionaryOverviewDisplay>(childItem);
                item.Level = level;
                list.Add(item);

                GetChildItemsForList(childItem, level + 1, list);
            }
        }

        private static Func<IDictionaryItem, string> ItemSort() => item => item.ItemKey;
    }
}
