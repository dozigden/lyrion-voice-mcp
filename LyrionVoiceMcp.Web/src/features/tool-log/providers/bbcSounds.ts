import { array, object, string } from '../../../shared/api/decoder';

export const providerSearchFields = {
  bbcSoundsSubscribed: array(object({ title: string, browseRef: string })),
  bbcSoundsStations: array(object({ name: string, browseRef: string, playRef: string }))
};

export function providerSearchGroups(result: {
  bbcSoundsSubscribed: { title: string; browseRef: string }[];
  bbcSoundsStations: { name: string; browseRef: string; playRef: string }[];
}) {
  return [
    { title: 'BBC Sounds subscriptions', items: result.bbcSoundsSubscribed },
    { title: 'BBC Sounds stations', items: result.bbcSoundsStations }
  ];
}
