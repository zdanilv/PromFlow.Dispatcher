using System;
using System.Collections.Generic;
using System.Text;

namespace Configurator.Application.Services.Authorization
{
    public interface IAuthApp
    {
        bool IsAuthenticated { get; set; }
        bool Authenticate(string username, string password);
    }
}
