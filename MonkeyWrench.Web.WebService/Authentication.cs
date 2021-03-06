﻿/*
 * Authentication.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2009 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Services;

using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;

namespace MonkeyWrench.WebServices
{
	public class Authentication {
		/// <summary>
		/// Authenticates the request with the provided user/pass.
		/// If no user/pass is provided, the method returns a response
		/// with no roles.
		/// If a wrong user/pass is provided, the method throws an exception.
		/// </summary>
		/// <param name="db"></param>
		/// <param name="login"></param>
		/// <param name="response"></param>
		public static void Authenticate (HttpContext Context, DB db, WebServiceLogin login, WebServiceResponse response, bool @readonly)
		{
			string ip = Context.Request.UserHostAddress;
			int person_id;
			DBLoginView view = null;

			Console.WriteLine ("WebService.Authenticate (Ip4: {0}, UserHostAddress: {1}, User: {2}, Cookie: {3}, Password: {4}", login == null ? null : login.Ip4, Context.Request.UserHostAddress, login == null ? null : login.User, login == null ? null : login.Cookie, login == null ? null : login.Password);

			// Check if credentials were passed in
			if (login == null || string.IsNullOrEmpty (login.User) || (string.IsNullOrEmpty (login.Password) && string.IsNullOrEmpty (login.Cookie))) {
				Console.WriteLine ("No credentials.");
				return;
			}

			if (@readonly && string.IsNullOrEmpty (login.Password)) {
				// Console.WriteLine ("Readonly authentication needs a password.");
				return;
			}

			if (!string.IsNullOrEmpty (login.Ip4)) {
				ip = login.Ip4;
			} else {
				ip = Context.Request.UserHostAddress;
			}

			if (@readonly) {
				DBLogin result = DBLogin_Extensions.Login (db, login.User, login.Password, ip, @readonly);
				if (result == null) {
					// Console.WriteLine ("Incorrect Login/Password for readonly login");
					return;
				}
				person_id = result.person_id;
			} else {
				if (!string.IsNullOrEmpty (login.Password)) {
					DBLogin result = DBLogin_Extensions.Login (db, login.User, login.Password, ip, @readonly);
					if (result != null)
						view = DBLoginView_Extensions.VerifyLogin (db, login.User, result.cookie, ip);
				} else {
					view = DBLoginView_Extensions.VerifyLogin (db, login.User, login.Cookie, ip);
					Console.WriteLine ("Verifying login, cookie: {0} user: {1} ip: {2}", login.Cookie, login.User, ip);
				}

				if (view == null) {
					Console.WriteLine ("Invalid credentials.");
					return;
				}
				person_id = view.person_id;
			}
			Console.WriteLine ("Valid credentials");

			LoginResponse login_response = response as LoginResponse;
			if (login_response != null) {
				login_response.Cookie = view != null ? view.cookie : null;
				login_response.FullName = view != null ? view.fullname : null;
				login_response.ID = person_id;
			}

			DBPerson person = DBPerson_Extensions.Create (db, person_id);

			Console.WriteLine ("Roles for '{0}': {1}", login.User, person.roles);

			if (!string.IsNullOrEmpty (person.roles))
				response.UserRoles = person.roles.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		public static void VerifyUserInRole (HttpContext Context, DB db, WebServiceLogin login, string role, bool @readonly)
		{
			WebServiceResponse dummy = new WebServiceResponse ();
			Authenticate (Context, db, login, dummy, @readonly);

			if (!dummy.IsInRole (role)) {
				Console.WriteLine ("The user '{0}' has the roles '{1}', and requested role is: {2}", login.User, dummy.UserRoles == null ? "<null>" : string.Join (",", dummy.UserRoles), role);
				throw new HttpException (403, "You don't have the required permissions.");
			}
		}
	}
}