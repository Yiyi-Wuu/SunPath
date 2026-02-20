import pandas as pd
from spa import getSolarPosition

latitude = 40.7128
longitude = -74.0060
altitude = 10

for hour in range(24):
    time_str = f"2026-02-07 {hour:02d}:00"
    time = pd.DatetimeIndex([pd.to_datetime(time_str).tz_localize("America/New_York")])
    
    solpos = getSolarPosition(time, latitude, longitude, altitude)
    zenith = solpos['zenith'].values[0]
    azimuth = solpos['azimuth'].values[0]
    
    print(f"{hour:02d}:00 -> zenith: {zenith:.2f}, azimuth: {azimuth:.2f}")
