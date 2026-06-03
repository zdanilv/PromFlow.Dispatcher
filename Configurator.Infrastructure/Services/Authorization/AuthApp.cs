using System;
using System.Collections.Generic;
using System.Text;
using Configurator.Application.Services.Authorization;
using ReactiveUI;

namespace Configurator.Infrastructure.Services.Authorization
{
    internal class AuthApp : ReactiveObject, IAuthApp
    {
        private bool _isAuthenticated;
        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            set => this.RaiseAndSetIfChanged(ref _isAuthenticated, value);
        }
        public bool Authenticate(string username, string password) 
        {             
            // Пример простой аутентификации (для демонстрации)
            if (username == "admin" && password == "password")
                IsAuthenticated = true;
            return IsAuthenticated;
        }
    }
}
