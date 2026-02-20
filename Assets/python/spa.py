import pandas as pd
import pvlib 

#time should be of the format: time = pd.DatetimeIndex(["2026-02-07 12:00"], tz="America/New_York")
#this function returns the apparent_zenith, zenith, apparent_elevation, elevation, azimuth, and equation_of_time
def getSolarPosition(time,latitude,longitude,altitude,method="nrel_numpy"):
  return pvlib.solarposition.get_solarposition(
    time=time,
    latitude=latitude,
    longitude=longitude,
    altitude=altitude,
    method=method
  )

def get_sunrise_sunset(dates, latitude, longitude, altitude=0, tz="America/New_York",fmt="%I:%M %p"):
    """
    Compute sunrise and sunset times for multiple dates.
    
    Parameters
    ----------
    dates : list of str or pd.Timestamp or pd.DatetimeIndex
        The dates for which to compute sunrise/sunset.
    latitude : float
        Latitude in decimal degrees.
    longitude : float
        Longitude in decimal degrees.
    altitude : float, optional
        Altitude in meters.
    tz : str, optional
        Timezone name, e.g., 'America/New_York'.
        
    Returns
    -------
    pd.DataFrame
        DataFrame with columns ['sunrise', 'sunset'] indexed by date.
    """
    
    # Ensure dates are timezone-aware Timestamps
    if isinstance(dates, list) or isinstance(dates, pd.DatetimeIndex):
        dates = pd.to_datetime(dates).tz_localize(tz, ambiguous='NaT', nonexistent='shift_forward')
    else:  # single timestamp
        dates = pd.DatetimeIndex([pd.to_datetime(dates).tz_localize(tz)])
    
    # Create location
    location = pvlib.location.Location(latitude=latitude, longitude=longitude, tz=tz, altitude=altitude)
    
    # Compute sunrise, sunset, transit
    sun_times = location.get_sun_rise_set_transit(dates)
    
    sun_times_formatted = sun_times[['sunrise', 'sunset']].copy()
    sun_times_formatted['sunrise'] = sun_times_formatted['sunrise'].dt.strftime(fmt)
    sun_times_formatted['sunset']  = sun_times_formatted['sunset'].dt.strftime(fmt)
    
    return sun_times

def get_the_irradiance(lat,lon,tz,altitude,dates):
    
    location = pvlib.location.Location(lat, lon, tz=tz, altitude=altitude)

    location = pvlib.location.Location(40.7128, -74.0060, tz="America/New_York", altitude=335)
    solpos = location.get_solarposition(dates,method="nrel_numpy")

    clearsky = location.get_clearsky(dates, model="ineichen",linke_turbidity=3.5)
    return clearsky

