﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Albireo.Base32;
using OtpSharp;
using ProAuth.Data;
using SQLite;

namespace ProAuth.Utilities
{
    internal class AuthSource
    {
        public string Search { get; set; }
        public SortType Sort { get; set; }

        public enum SortType
        {
            Alphabetical, CreatedDate
        };

        private readonly SQLiteConnection _connection;
        private List<Authenticator> _cache;

        public AuthSource(SQLiteConnection connection)
        {
            Search = "";
            Sort = SortType.Alphabetical;

            _connection = connection;
            _cache = new List<Authenticator>();
        }

        public Authenticator GetNth(int n)
        {
            string sql = $@"SELECT * FROM authenticator ";

            Authenticator auth;
            bool searching = Search.Trim() != "";

            if(searching)
            {
                sql += "WHERE issuer LIKE ? ";
            }

            sql += GetOrderStatement() + $@" LIMIT 1 OFFSET {n}";
            object[] args = {$@"%{Search}%"};

            if(_cache.Count <= n || _cache[n] == null)
            {
                auth = searching
                    ? _connection.Query<Authenticator>(sql, args).First()
                    : _connection.Query<Authenticator>(sql).First();

                _cache.Insert(n, auth);
            }
            else
            {
                auth = _cache[n];
            }

            if(auth.Type == OtpType.Totp && auth.TimeRenew < DateTime.Now)
            {
                auth = searching
                    ? _connection.Query<Authenticator>(sql, args).First()
                    : _connection.Query<Authenticator>(sql).First();

                byte[] secret = Base32.Decode(auth.Secret);
                Totp totp = new Totp(secret, auth.Period, auth.Algorithm, auth.Digits);
                auth.Code = totp.ComputeTotp();
                auth.TimeRenew = DateTime.Now.AddSeconds(totp.RemainingSeconds());

                _connection.Update(auth);
                _cache[n] = auth;
            }

            return auth;
        }

        private string GetOrderStatement()
        {
            switch(Sort)
            {
                case SortType.Alphabetical:
                    return "ORDER BY issuer ASC, username ASC";

                case SortType.CreatedDate:
                    return "ORDER BY id ASC";

                default:
                    return "";
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public void ClearCache(int position)
        {
            _cache[position] = null;
        }

        public void Delete(int n)
        {
            string sql = $@"SELECT * FROM authenticator LIMIT 1 OFFSET {n}";
            Authenticator auth = _connection.Query<Authenticator>(sql).First();
            _cache = new List<Authenticator>();
            _connection.Delete<Authenticator>(auth.Secret);
        }

        public void RefreshHotp(int n)
        {
            Authenticator auth = GetNth(n);
            byte[] secret = Base32.Decode(auth.Secret);
            Hotp hotp = new Hotp(secret, auth.Algorithm);

            auth.Counter++;
            auth.Code = hotp.ComputeHotp(auth.Counter);
            auth.TimeRenew = DateTime.Now.AddSeconds(10);

            _cache[n] = auth;
            _connection.Update(auth);
        }

        public bool IsDuplicate(Authenticator auth)
        {
            Authenticator existing;

            try
            {
                existing = _connection.Get<Authenticator>(auth.Secret);
            }
            catch
            {
                return false;
            }

            return existing.Type == auth.Type;
        }

        public int Count()
        {
            if(Search.Trim() == "")
            {
                return _connection.Table<Authenticator>().Count();
            }

            string sql = "SELECT id FROM authenticator WHERE issuer LIKE ?";
            object[] args = {$@"%{Search}%"};

            return _connection.Query<Authenticator>(sql, args).Count();
        }
    }
}