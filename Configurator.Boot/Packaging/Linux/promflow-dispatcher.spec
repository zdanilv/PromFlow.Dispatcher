Name: promflow-dispatcher
Version: %{app_version}
Release: alt1
Summary: PromFlow Dispatcher desktop application
License: MIT
Group: Graphical desktop/Other
Url: https://github.com/zdanilv/PromFlow.Dispatcher
Source0: %{name}-%{version}.tar.gz
ExclusiveArch: x86_64

Requires: libX11
Requires: libICE
Requires: libSM
Requires: fontconfig
Requires: libicu
Requires: openssl
Requires: libkrb5
Requires: zlib
Requires: ca-certificates
Requires: tzdata
Requires: xterm

%description
PromFlow Dispatcher is an Avalonia desktop application for RouteMap,
Modbus TCP and OPC UA configuration and operation.

%prep
%setup -q

%build

%install
mkdir -p %{buildroot}/opt/promflow-dispatcher
cp -a app/. %{buildroot}/opt/promflow-dispatcher/

install -D -m 0644 promflow-dispatcher.desktop \
    %{buildroot}/usr/share/applications/promflow-dispatcher.desktop
install -D -m 0644 promflow-dispatcher.png \
    %{buildroot}/usr/share/icons/hicolor/256x256/apps/promflow-dispatcher.png
mkdir -p %{buildroot}%{_bindir}
ln -s /opt/promflow-dispatcher/PromFlow.Dispatcher \
    %{buildroot}%{_bindir}/promflow-dispatcher

desktop-file-validate \
    %{buildroot}/usr/share/applications/promflow-dispatcher.desktop

%files
%doc LICENSE.txt
/opt/promflow-dispatcher
%{_bindir}/promflow-dispatcher
/usr/share/applications/promflow-dispatcher.desktop
/usr/share/icons/hicolor/256x256/apps/promflow-dispatcher.png

%changelog
* Fri Jul 17 2026 PromFlow Release Team <release@promflow.local> %{version}-alt1
- Initial local package for ALT Linux 11.1.
