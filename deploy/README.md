# Orange Pi deployment

Publish from the repository on Windows with the .NET 10 SDK. This bundles the
Linux ARM64 runtime and overrides Native AOT for this build.

```powershell
dotnet publish -c Release -r linux-arm64 --self-contained true -p:PublishAot=false -o bin/pi-linux-arm64
tar.exe -czf bin/picture-monitor-linux-arm64.tar.gz -C bin/pi-linux-arm64 .
scp bin/picture-monitor-linux-arm64.tar.gz deploy/picture-monitor.service orangepi@192.168.1.171:~/
```

Run these commands over SSH as `orangepi`. The service uses the local console
`tty1` for image display. If you need a local text login, switch to `tty2` with
Ctrl+Alt+F2. Stop any manually started server on port 5159 before installation.

```sh
sudo apt install fim
sudo systemctl mask --now getty@tty1.service
sudo install -d /opt/picture-monitor
sudo tar -xzf ~/picture-monitor-linux-arm64.tar.gz -C /opt/picture-monitor
sudo chmod +x /opt/picture-monitor/picture_monitor
sudo install -m 0644 ~/picture-monitor.service /etc/systemd/system/picture-monitor.service
sudo systemctl daemon-reload
sudo systemctl enable --now picture-monitor.service
sudo systemctl restart picture-monitor.service
```

The server starts at boot and opens the latest saved image with `fim` on
`/dev/fb0`. Each successful upload replaces the viewer directly. Switch to
`tty1` with Ctrl+Alt+F1 to view it if another virtual console is active.
For upgrades, remove the obsolete desktop autostart entry.

```sh
rm -f ~/.config/autostart/picture-monitor-kiosk.desktop
```

Open `http://192.168.1.171:5159/` from the management computer. The upload
endpoint has no login and is intended for a trusted LAN. Each successful upload
becomes the active image and the five newest images are stored under
`/var/lib/picture-monitor`. To import an image saved by the previous version,
run this once after the new service starts if that file exists.

```sh
curl --fail --data-binary @/home/orangepi/current_image.jpg http://127.0.0.1:5159/upload
```

Use `systemctl status picture-monitor` and `journalctl -u picture-monitor -b`
to check the service after boot.
