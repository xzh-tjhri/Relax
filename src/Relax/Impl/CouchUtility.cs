﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using Relax.Config;
using Relax.Impl.Commands;
using Relax.Impl.Http;
using Relax.Impl.Json;
using Relax.Impl.Model;
using Symbiote.Core.Extensions;
using Symbiote.Core.Reflection;
using Symbiote.Core.Utility;
using System.Linq;
using Relax.Impl;

namespace Relax.Impl
{
    public class CouchUtility
    {
        protected ICouchConfiguration configuration { get; set; }
        protected CouchCommandFactory commandFactory { get; set; }
        protected ConcurrentDictionary<string, bool> _databaseExists = new ConcurrentDictionary<string, bool>();

        public virtual string GetDocumentIdAsJson(object instance)
        {
            var doc = instance as IHaveDocumentId;
            if (doc != null)
            {
                return doc.GetDocumentIdAsJson();
            }
            else
            {
                return Reflector.ReadMember(instance, configuration.Conventions.IdPropertyName).ToJson(false);
            }
        }

        public virtual object GetDocumentId(object instance)
        {
            var doc = instance as IHaveDocumentId;
            if (doc != null)
            {
                return doc.GetDocumentId();
            }
            else
            {
                return Reflector.ReadMember(instance, configuration.Conventions.IdPropertyName);
            }
        }

        public virtual string GetDocumentRevision(object instance)
        {
            var doc = instance as IHaveDocumentRevision;
            if (doc != null)
            {
                return doc.DocumentRevision;
            }
            else
            {
                var documentRevision = Reflector.ReadMember(instance, configuration.Conventions.RevisionPropertyName).ToString();
                return string.IsNullOrEmpty(documentRevision) ? null : documentRevision;
            }
        }

        public virtual void SetDocumentRevision(string revision, object instance)
        {
            var doc = instance as IHaveDocumentRevision;
            if (doc != null)
            {
                doc.UpdateRevFromJson(revision);
            }
            else
            {
                Reflector.WriteMember(instance, configuration.Conventions.RevisionPropertyName, revision);
            }
        }
        
        public virtual void EnsureDatabaseExists<TModel>()
        {
            var database = configuration.GetDatabaseNameForType<TModel>();
            EnsureDatabaseExists(database);
        }

        public virtual void CreateDatabase<TModel>()
        {
            var database = configuration.GetDatabaseNameForType<TModel>();
            CreateDatabase(database);
        }

        public virtual bool DatabaseExists<TModel>()
        {
            var database = configuration.GetDatabaseNameForType<TModel>();
            return DatabaseExists(database);
        }

        public virtual void EnsureDatabaseExists(string database)
        {
            if (string.IsNullOrEmpty(database))
                return;

            var dbCreated = false;
            var shouldCheckCouch = false;
            ServerCommand command = null;
            try
            {
                shouldCheckCouch = !_databaseExists.TryGetValue(database, out dbCreated);
                if (shouldCheckCouch && !dbCreated)
                {
                    command = commandFactory.GetServerCommand();
                    command.CreateDatabase(database);
                    _databaseExists[database] = true;
                }
            }
            catch (WebException webEx)
            {
                if (webEx.Message.Contains("(412) Precondition Failed"))
                {
                    _databaseExists[database] = true;
                }
                else
                {
                    "An exception occurred while trying to check for the existence of database {0} at uri {1}. \r\n\t {2}"
                        .ToError<IDocumentRepository>(database, command.Uri, webEx);
                    throw;
                }

            }
            catch (Exception ex)
            {
                "An exception occurred while trying to check for the existence of database {0} at uri {1}. \r\n\t {2}"
                    .ToError<IDocumentRepository>(database, command.Uri, ex);
                throw;
            }
        }

        public virtual void CreateDatabase(string database)
        {
            var command = commandFactory.GetServerCommand();
            command.CreateDatabase(database);
            _databaseExists[database] = true;
        }

        public virtual bool DatabaseExists(string database)
        {
            var command = commandFactory.GetServerCommand();
            var exists = command.DatabaseExists(database);
            _databaseExists[command.Uri.DatabaseName] = exists;
            return exists;
        }

        public virtual CouchUri NewUri()
        {
            var uri = configuration.Preauthorize
                              ? CouchUri.Build(
                                  configuration.User,
                                  configuration.Password,
                                  configuration.Protocol,
                                  configuration.Server,
                                  configuration.Port)
                              : CouchUri.Build(
                                  configuration.Protocol,
                                  configuration.Server,
                                  configuration.Port);
            uri.EnsureDatabaseExists();
            return uri;
        }
        public virtual CouchUri NewUri<TModel>()
        {
            var database = configuration.GetDatabaseNameForType<TModel>();
            return NewUri(database);
        }
        public virtual CouchUri NewUri(string database)
        {
            var uri = configuration.Preauthorize ?
                                CouchUri.Build(
                                    configuration.User,
                                    configuration.Password,
                                    configuration.Protocol,
                                    configuration.Server,
                                    configuration.Port,
                                    database)
                              : CouchUri.Build(
                                  configuration.Protocol,
                                  configuration.Server,
                                  configuration.Port,
                                  database);
            uri.EnsureDatabaseExists();
            return uri;
        }

        public virtual object[] GetDocumentGraph(object model)
        {
            object[] documentArray = new object[] {};
            List<object> original = new List<object>();
            if(model as IEnumerable != null)
            {
                original.AddRange((model as IEnumerable).Cast<object>());
            }
            else
            {
                original.Add(model);
            }

            if (configuration.BreakDownDocumentGraphs)
            {
                var watcher = new DocumentHierarchyWatcher();
                var visitor = new HierarchyVisitor(IsDocument);
                visitor.Subscribe(watcher);
                visitor.Visit(model);
                documentArray = watcher.Documents.ToArray();
            }

            return documentArray.Length == 0 ? original.ToArray() : documentArray;
        }

        public virtual bool IsDocument(object instance)
        {
            return instance.GetType().GetInterface("ICouchDocument`1") != null;
        }

        public virtual string GetDatabaseForType<TModel>()
        {
            return configuration.GetDatabaseNameForType<TModel>();
        }

        public CouchUtility(ICouchConfiguration couchConfiguration)
        {
            configuration = couchConfiguration;
            commandFactory = new CouchCommandFactory();
        }
    }
}